using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Dashboard;

/// <summary>M6c 6.10：链路泳道单个节点。count 一律来自真实查询，不编造。</summary>
public sealed record PipelineNodeDto(
    string Key,
    string Label,
    string Middleware,
    long Count,
    string Unit,
    string Source,
    bool Available,
    string Link);

public sealed record PipelineSnapshotDto(
    DateTime GeneratedAtUtc,
    IReadOnlyList<PipelineNodeDto> Nodes);

/// <summary>
/// M6c：为运维后台泳道图提供 7 个节点的真实计数。
/// 事实源依次是 Orders / OutboxMessages / RabbitMQ q.fulfillment 深度 / ProcessedEvents /
/// InventoryLedgers / DeadLetters / DailyReportRecords。
/// 队列深度不可用时该节点返回 count=-1 + available=false，由前端显示「—」，绝不伪造 0。
/// </summary>
public sealed class PipelineService
{
    private readonly PulseDbContext _db;
    private readonly IQueueDepthProvider _queues;

    public PipelineService(PulseDbContext db, IQueueDepthProvider queues)
    {
        _db = db;
        _queues = queues;
    }

    public async Task<PipelineSnapshotDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders.CountAsync(cancellationToken);
        var outboxPending = await _db.OutboxMessages
            .CountAsync(m => m.Status == OutboxPublisher.StatusPending, cancellationToken);
        var depth = await _queues.GetAsync(cancellationToken);
        var processed = await _db.ProcessedEvents.CountAsync(cancellationToken);
        var deduct = await _db.InventoryLedgers
            .CountAsync(l => l.ChangeType == "Deduct", cancellationToken);
        var deadLetterPending = await _db.DeadLetters
            .CountAsync(d => d.Status == DeadLetterService.StatusPending, cancellationToken);
        var reports = await _db.DailyReportRecords.CountAsync(cancellationToken);

        var nodes = new List<PipelineNodeDto>
        {
            new("orders", "下单事务", "MySQL", orders, "笔订单",
                "orders + order_items + inventory_ledgers + outbox（同事务）", true, "/admin/orders"),
            new("outbox", "Outbox 待投递", "MySQL", outboxPending, "条待投递",
                "outbox (status=pending) → OutboxPublisher", true, "/admin/orders"),
            new("exchange", "消息投递", "RabbitMQ",
                depth.Available ? depth.Fulfillment : -1, "条在队",
                "ex.orders → q.fulfillment（QueueDeclarePassive）", depth.Available, "/admin/lab"),
            new("worker", "消费判重", "Redis", processed, "条已处理",
                "dedup:{event_id} SETNX + processed_events 唯一键", true, "/admin/lab"),
            new("deduct", "库存实扣", "MySQL", deduct, "条 Deduct 流水",
                "inventory_ledgers (ChangeType=Deduct) + Inventory.Version", true, "/admin/products"),
            new("deadLetter", "死信待补偿", "RabbitMQ", deadLetterPending, "条待处理",
                "q.dlq.fulfillment → dead_letters (status=pending)", true, "/admin/dead-letters"),
            new("report", "日报落表", "MySQL", reports, "份已落表",
                "daily_reports (stat_date 幂等 upsert)", true, "/admin/reports")
        };

        return new PipelineSnapshotDto(DateTime.UtcNow, nodes);
    }
}
