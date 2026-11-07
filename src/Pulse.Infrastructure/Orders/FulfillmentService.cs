using System.Linq;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Orders;

/// <summary>
/// 履约消费（SPEC 第 5/6 节）：支付后实扣——reserved -= qty、available 不变，
/// 写 Deduct 流水，订单 Paid -> Shipped。
/// M6b v0.2：有明细时按 order_items 逐行实扣（DB 是事实源）；无明细的历史 /
/// 探针订单回退到 payload 的 sku/quantity，保持 M3 单消息语义不变。
/// 状态流转只走 <see cref="OrderStateMachine.Transition"/>；乐观锁 + 单 SaveChanges
/// 保证「状态 + 库存 + 流水」同事务，重复消息不重复扣。
/// </summary>
public sealed class FulfillmentService
{
    private readonly PulseDbContext _db;

    public FulfillmentService(PulseDbContext db) => _db = db;

    public async Task FulfillAsync(
        OrderEvent orderEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderEvent);

        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNo == orderEvent.OrderNo, cancellationToken)
            ?? throw new InvalidOperationException($"订单不存在: {orderEvent.OrderNo}");

        // 已履约（Shipped 及之后）幂等跳过：重复消息不重复扣库存、不重复写流水。
        if (order.Status is OrderStatus.Shipped or OrderStatus.Completed
            or OrderStatus.Refunding or OrderStatus.Refunded)
        {
            return;
        }

        // 状态流转唯一入口：Created / Cancelled -> Shipped 会抛 InvalidOperationException。
        order.Status = OrderStateMachine.Transition(order.Status, OrderStatus.Shipped);

        var lines = order.Items.Count > 0
            ? order.Items.Select(i => (Sku: i.Sku, Quantity: i.Quantity)).ToList()
            : new List<(string Sku, int Quantity)> { (orderEvent.Sku, orderEvent.Quantity) };

        var now = DateTime.UtcNow;
        foreach (var line in lines)
        {
            var inventory = await _db.Inventories
                .FirstOrDefaultAsync(x => x.Sku == line.Sku, cancellationToken)
                ?? throw new InvalidOperationException($"库存不存在: {line.Sku}");

            inventory.Reserved -= line.Quantity;
            inventory.Version += 1;

            _db.InventoryLedgers.Add(new InventoryLedger
            {
                Sku = line.Sku,
                ChangeType = "Deduct",
                Quantity = line.Quantity,
                RefOrderId = order.Id,
                CreatedAtUtc = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
