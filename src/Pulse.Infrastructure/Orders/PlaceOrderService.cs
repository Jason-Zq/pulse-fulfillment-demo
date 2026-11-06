using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Orders;

public sealed record PlaceOrderCommand(string Sku, int Quantity, string IdempotencyKey);
public sealed record PlaceOrderResult(string OrderNo, OrderStatus Status);

public sealed class InsufficientStockException(string sku)
    : Exception($"库存不足: {sku}");

public sealed class StockConcurrencyException(string sku, Exception inner)
    : Exception($"库存并发冲突，请重试: {sku}", inner);

public sealed class PlaceOrderService
{
    private static readonly TimeSpan OrderTtl = TimeSpan.FromMinutes(15);

    private readonly PulseDbContext _db;

    public PlaceOrderService(PulseDbContext db) => _db = db;

    public async Task<PlaceOrderResult> PlaceAsync(
        PlaceOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.Quantity), "数量必须大于 0");
        }
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new ArgumentException("缺少幂等键", nameof(command));
        }

        var existing = await _db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return new PlaceOrderResult(existing.OrderNo, existing.Status);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var product = await _db.Products.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Sku == command.Sku && x.IsActive, cancellationToken)
                ?? throw new InvalidOperationException($"商品不存在或已下架: {command.Sku}");

            var inventory = await _db.Inventories
                .FirstOrDefaultAsync(x => x.Sku == command.Sku, cancellationToken)
                ?? throw new InsufficientStockException(command.Sku);

            if (inventory.Available < command.Quantity)
            {
                throw new InsufficientStockException(command.Sku);
            }

            var now = DateTime.UtcNow;
            var orderNo = $"PO{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
            var order = new Order
            {
                OrderNo = orderNo,
                IdempotencyKey = command.IdempotencyKey,
                TotalAmount = product.Price * command.Quantity,
                Status = OrderStatus.Created,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(OrderTtl),
                Version = 0,
                Items =
                {
                    new OrderItem
                    {
                        Sku = command.Sku,
                        Quantity = command.Quantity,
                        UnitPrice = product.Price
                    }
                }
            };

            inventory.Available -= command.Quantity;
            inventory.Reserved += command.Quantity;
            inventory.Version += 1;

            var ledger = new InventoryLedger
            {
                Sku = command.Sku,
                ChangeType = "Reserve",
                Quantity = command.Quantity,
                CreatedAtUtc = now
            };

            var message = new OutboxMessage
            {
                EventId = Guid.NewGuid().ToString("N"),
                RoutingKey = "order.created",
                Payload = JsonSerializer.Serialize(new
                {
                    orderNo,
                    sku = command.Sku,
                    quantity = command.Quantity,
                    expiresAtUtc = order.ExpiresAtUtc
                }),
                Status = "pending",
                RetryCount = 0,
                CreatedAtUtc = now
            };

            _db.Orders.Add(order);
            _db.InventoryLedgers.Add(ledger);
            _db.OutboxMessages.Add(message);
            await _db.SaveChangesAsync(cancellationToken);

            ledger.RefOrderId = order.Id;
            await _db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new PlaceOrderResult(orderNo, OrderStatus.Created);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new StockConcurrencyException(command.Sku, ex);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
