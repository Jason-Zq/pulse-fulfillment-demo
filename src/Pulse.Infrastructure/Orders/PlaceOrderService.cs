using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Orders;

public sealed record PlaceOrderCommand(string Sku, int Quantity, string IdempotencyKey);
public sealed record PlaceOrderResult(long Id, string OrderNo, OrderStatus Status);

/// <summary>M6b v0.2：多明细下单的一行；同 SKU 重复行会在服务内合并。</summary>
public sealed record PlaceOrderLine(string Sku, int Quantity);

/// <summary>M6b v0.2：与单 SKU 路径并存的多明细命令，二者由 API 层保证互斥。</summary>
public sealed record PlaceMultiOrderCommand(IReadOnlyList<PlaceOrderLine> Lines, string IdempotencyKey);

public sealed class InsufficientStockException(string sku)
    : Exception($"库存不足: {sku}");

public sealed class StockConcurrencyException(string sku, Exception inner)
    : Exception($"库存并发冲突，请重试: {sku}", inner);

public sealed class PlaceOrderService
{
    private static readonly TimeSpan OrderTtl = TimeSpan.FromMinutes(15);

    private readonly PulseDbContext _db;

    public PlaceOrderService(PulseDbContext db) => _db = db;

    /// <summary>M1 单 SKU 路径。M6b 起保留原样不动，132 条容器断言与 E1/E2/E3 走这条路径。</summary>
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
            return new PlaceOrderResult(existing.Id, existing.OrderNo, existing.Status);
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
            return new PlaceOrderResult(order.Id, orderNo, OrderStatus.Created);
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

    /// <summary>
    /// M6b v0.2 多明细路径：同一 MySQL 事务写多行 order_items + 逐 SKU 预扣 /
    /// Reserve 流水 + 一行 order.created outbox。先全量校验再改库存，任一 SKU
    /// 不存在、下架或库存不足都整单回滚，不留部分预扣。
    /// </summary>
    public async Task<PlaceOrderResult> PlaceMultiAsync(
        PlaceMultiOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new ArgumentException("缺少幂等键", nameof(command));
        }
        if (command.Lines is null || command.Lines.Count == 0)
        {
            throw new ArgumentException("items 不能为空", nameof(command));
        }
        if (command.Lines.Any(l => string.IsNullOrWhiteSpace(l.Sku) || l.Quantity <= 0))
        {
            throw new ArgumentException("每项需提供有效 sku 且 quantity > 0", nameof(command));
        }

        // 同 SKU 合并，避免同一库存行被重复更新触发乐观锁冲突。
        var merged = command.Lines
            .GroupBy(l => l.Sku, StringComparer.Ordinal)
            .Select(g => new PlaceOrderLine(g.Key, g.Sum(l => l.Quantity)))
            .ToList();

        var existing = await _db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return new PlaceOrderResult(existing.Id, existing.OrderNo, existing.Status);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var skus = merged.Select(l => l.Sku).ToList();
            var products = await _db.Products.AsNoTracking()
                .Where(p => skus.Contains(p.Sku) && p.IsActive)
                .ToDictionaryAsync(p => p.Sku, cancellationToken);
            var inventories = await _db.Inventories
                .Where(i => skus.Contains(i.Sku))
                .ToDictionaryAsync(i => i.Sku, cancellationToken);

            foreach (var line in merged)
            {
                if (!products.ContainsKey(line.Sku))
                {
                    throw new ArgumentException($"商品不存在或已下架: {line.Sku}", nameof(command));
                }
                if (!inventories.TryGetValue(line.Sku, out var inv) || inv.Available < line.Quantity)
                {
                    throw new InsufficientStockException(line.Sku);
                }
            }

            var now = DateTime.UtcNow;
            var orderNo = $"PO{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
            var order = new Order
            {
                OrderNo = orderNo,
                IdempotencyKey = command.IdempotencyKey,
                TotalAmount = 0m,
                Status = OrderStatus.Created,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(OrderTtl),
                Version = 0
            };

            var ledgers = new List<InventoryLedger>();
            foreach (var line in merged)
            {
                var price = products[line.Sku].Price;
                order.Items.Add(new OrderItem
                {
                    Sku = line.Sku,
                    Quantity = line.Quantity,
                    UnitPrice = price
                });
                order.TotalAmount += price * line.Quantity;

                var inventory = inventories[line.Sku];
                inventory.Available -= line.Quantity;
                inventory.Reserved += line.Quantity;
                inventory.Version += 1;

                var ledger = new InventoryLedger
                {
                    Sku = line.Sku,
                    ChangeType = "Reserve",
                    Quantity = line.Quantity,
                    CreatedAtUtc = now
                };
                ledgers.Add(ledger);
                _db.InventoryLedgers.Add(ledger);
            }

            // 一行 outbox；sku/quantity 保留第一明细以兼容 M1/M3 消费者，
            // 另带 items 数组让消息自描述多明细（消费端仍以 order_items 为事实源）。
            var message = new OutboxMessage
            {
                EventId = Guid.NewGuid().ToString("N"),
                RoutingKey = "order.created",
                Payload = JsonSerializer.Serialize(new
                {
                    orderNo,
                    sku = merged[0].Sku,
                    quantity = merged[0].Quantity,
                    expiresAtUtc = order.ExpiresAtUtc,
                    items = merged.Select(l => new { sku = l.Sku, quantity = l.Quantity }).ToArray()
                }),
                Status = "pending",
                RetryCount = 0,
                CreatedAtUtc = now
            };

            _db.Orders.Add(order);
            _db.OutboxMessages.Add(message);
            await _db.SaveChangesAsync(cancellationToken);

            foreach (var ledger in ledgers)
            {
                ledger.RefOrderId = order.Id;
            }
            await _db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new PlaceOrderResult(order.Id, orderNo, OrderStatus.Created);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new StockConcurrencyException(merged[0].Sku, ex);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
