using System.Linq;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Orders;

/// <summary>
/// 超时关单（SPEC 第 5/6 节，A7 的逻辑层）：order.timeout 消息到期后，
/// Created 且已过 ExpiresAtUtc 的订单 -> Cancelled，reserved 释放回 available，
/// 写 Release 流水。状态流转只走 <see cref="OrderStateMachine.Transition"/>。
/// M6b v0.2：有明细时按 order_items 逐行释放（DB 是事实源）；无明细的历史 /
/// 探针订单回退到 payload 的 sku/quantity，保持 M3 单消息语义不变。
/// </summary>
public sealed class TimeoutOrderService
{
    private readonly PulseDbContext _db;

    public TimeoutOrderService(PulseDbContext db) => _db = db;

    /// <returns>是否真的执行了关单；重复 / 未到期 / 非 Created 返回 false。</returns>
    public async Task<bool> CloseExpiredAsync(
        OrderEvent orderEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderEvent);

        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNo == orderEvent.OrderNo, cancellationToken)
            ?? throw new InvalidOperationException($"订单不存在: {orderEvent.OrderNo}");

        // 已支付 / 已取消等幂等跳过，重复消息不重复释放库存。
        if (order.Status != OrderStatus.Created)
        {
            return false;
        }

        // TTL 提前触发时不动，未真正到期不关单。
        if (order.ExpiresAtUtc > DateTime.UtcNow)
        {
            return false;
        }

        order.Status = OrderStateMachine.Transition(order.Status, OrderStatus.Cancelled);

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
            inventory.Available += line.Quantity;
            inventory.Version += 1;

            _db.InventoryLedgers.Add(new InventoryLedger
            {
                Sku = line.Sku,
                ChangeType = "Release",
                Quantity = line.Quantity,
                RefOrderId = order.Id,
                CreatedAtUtc = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
