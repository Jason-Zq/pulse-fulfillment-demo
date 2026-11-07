using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Orders;

/// <summary>
/// 支付入口（SPEC 第 7 节 /api/orders/{id}/pay）：Created -> Paid，写 PaidAtUtc，
/// 并在同一事务写一条 order.paid 的 outbox 消息，交由 OutboxPublisher 投递履约。
/// eventId 用 OutboxMessage.EventId，payload 只放业务字段（与 M1 PlaceOrderService 一致）。
/// </summary>
public sealed class OrderLifecycleService
{
    private readonly PulseDbContext _db;

    public OrderLifecycleService(PulseDbContext db) => _db = db;

    public async Task<OrderStatus> PayAsync(
        long orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException($"订单不存在: {orderId}");

        // 已支付及之后幂等返回，不重复写 outbox。
        if (order.Status is OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.Completed
            or OrderStatus.Refunding or OrderStatus.Refunded)
        {
            return order.Status;
        }

        // 状态流转唯一入口：Cancelled -> Paid 抛 InvalidOperationException。
        order.Status = OrderStateMachine.Transition(order.Status, OrderStatus.Paid);
        var now = DateTime.UtcNow;
        order.PaidAtUtc = now;

        var item = order.Items.FirstOrDefault();
        _db.OutboxMessages.Add(new OutboxMessage
        {
            EventId = Guid.NewGuid().ToString("N"),
            RoutingKey = OrderTopology.RoutingKeyOrderPaid,
            Payload = JsonSerializer.Serialize(new
            {
                orderNo = order.OrderNo,
                sku = item?.Sku ?? string.Empty,
                quantity = item?.Quantity ?? 0,
                expiresAtUtc = order.ExpiresAtUtc,
                // M6b：多明细订单的消息自描述；消费端仍以 order_items 为事实源。
                items = order.Items
                    .Select(i => new { sku = i.Sku, quantity = i.Quantity })
                    .ToArray()
            }),
            Status = "pending",
            RetryCount = 0,
            CreatedAtUtc = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        return order.Status;
    }

    /// <summary>
    /// 取消订单（SPEC 第 7 节 /api/orders/{id}/cancel）：Created -> Cancelled，
    /// 释放预扣（available += qty、reserved -= qty），写一条 Release 流水。
    /// 状态流转只走 <see cref="OrderStateMachine.Transition"/>；库存走乐观锁，
    /// 订单状态 / 库存 / 流水在一次 SaveChanges 内同事务提交。重复取消幂等。
    /// </summary>
    public async Task<OrderStatus> CancelAsync(
        long orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException($"订单不存在: {orderId}");

        // 已取消幂等返回，不重复释放库存、不重复写流水。
        if (order.Status == OrderStatus.Cancelled)
        {
            return order.Status;
        }

        // 状态流转唯一入口：Paid / Shipped / ... -> Cancelled 抛 InvalidOperationException。
        order.Status = OrderStateMachine.Transition(order.Status, OrderStatus.Cancelled);
        order.Version += 1;

        var now = DateTime.UtcNow;
        foreach (var item in order.Items)
        {
            var inventory = await _db.Inventories
                .FirstOrDefaultAsync(x => x.Sku == item.Sku, cancellationToken)
                ?? throw new InvalidOperationException($"库存不存在: {item.Sku}");

            inventory.Available += item.Quantity;
            inventory.Reserved -= item.Quantity;
            inventory.Version += 1;

            _db.InventoryLedgers.Add(new InventoryLedger
            {
                Sku = item.Sku,
                ChangeType = "Release",
                Quantity = item.Quantity,
                RefOrderId = order.Id,
                CreatedAtUtc = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return order.Status;
    }

    /// <summary>
    /// 发货（SPEC 第 7 节 /api/orders/{id}/ship）：Paid -> Shipped，只做状态流转；
    /// 库存实扣由 M3 FulfillmentService 消费 order.paid 承担，此处不重复扣减、不写库存流水。
    /// 状态流转只走 <see cref="OrderStateMachine.Transition"/>。
    /// </summary>
    public async Task<OrderStatus> ShipAsync(
        long orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException($"订单不存在: {orderId}");

        // 已发货及之后幂等返回，不重复流转。
        if (order.Status is OrderStatus.Shipped or OrderStatus.Completed
            or OrderStatus.Refunding or OrderStatus.Refunded)
        {
            return order.Status;
        }

        // 状态流转唯一入口：Created / Cancelled -> Shipped 抛 InvalidOperationException。
        order.Status = OrderStateMachine.Transition(order.Status, OrderStatus.Shipped);
        order.Version += 1;

        await _db.SaveChangesAsync(cancellationToken);
        return order.Status;
    }
}
