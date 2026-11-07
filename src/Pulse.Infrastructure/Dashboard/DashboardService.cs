using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Alerts;
using Pulse.Infrastructure.Lab;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;
using Pulse.Infrastructure.Reports;

namespace Pulse.Infrastructure.Dashboard;

/// <summary>进程启动时从配置读取的依赖状态，如实写入 source 字符串。</summary>
public sealed record DashboardEnvironment(string Database, bool RedisConfigured, bool RabbitConfigured);

public sealed record DashboardToday(int OrderCnt, int PaidCnt, int CancelCnt, decimal Gmv);
public sealed record DashboardInventory(int SkuCount, int LowStockCount, int TotalAvailable, int LowStockThreshold);
public sealed record DashboardQueues(long Fulfillment, long OrderTimeout, long FulfillmentRetry, long Dlq, bool Available);
public sealed record DashboardControls(bool ConsumerEnabled, bool PublisherEnabled, bool FailureInjectionEnabled);
public sealed record DashboardSource(string Database, string Redis, string Rabbitmq);

public sealed record DashboardSnapshot(
    DateTime GeneratedAtUtc,
    DashboardToday Today,
    DashboardInventory Inventory,
    DashboardQueues Queues,
    int OutboxPending,
    int DeadLetterPending,
    int AlertsActive,
    DashboardControls Controls,
    DashboardSource Source);

/// <summary>
/// M5 5.5：聚合今日订单 / GMV / 取消数、库存三态、outbox pending、死信 pending、
/// 活跃告警与控制开关；队列深度不可用时不伪造 0（返回 -1 并标注 available:false）。
/// 今日口径复用 DailyReportService，保证与日报端点一致。
/// </summary>
public sealed class DashboardService
{
    private readonly PulseDbContext _db;
    private readonly DailyReportService _reports;
    private readonly IQueueDepthProvider _queues;
    private readonly ILabControlStore _controls;
    private readonly DashboardEnvironment _env;

    public DashboardService(
        PulseDbContext db,
        DailyReportService reports,
        IQueueDepthProvider queues,
        ILabControlStore controls,
        DashboardEnvironment env)
    {
        _db = db;
        _reports = reports;
        _queues = queues;
        _controls = controls;
        _env = env;
    }

    public async Task<DashboardSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = await _reports.GetAsync(today, cancellationToken);
        var todayDto = new DashboardToday(
            report.OrderCnt, report.PaidCnt, report.CancelCnt, report.Gmv);

        var availables = await _db.Inventories.AsNoTracking()
            .Select(i => i.Available)
            .ToListAsync(cancellationToken);
        var inventory = new DashboardInventory(
            availables.Count,
            availables.Count(a => a < AlertService.LowStockThreshold),
            availables.Sum(),
            AlertService.LowStockThreshold);

        var depth = await _queues.GetAsync(cancellationToken);
        var queues = new DashboardQueues(
            depth.Fulfillment, depth.OrderTimeout, depth.FulfillmentRetry, depth.Dlq,
            depth.Available);

        var outboxPending = await _db.OutboxMessages
            .CountAsync(m => m.Status == OutboxPublisher.StatusPending, cancellationToken);
        var deadLetterPending = await _db.DeadLetters
            .CountAsync(d => d.Status == DeadLetterService.StatusPending, cancellationToken);
        var alertsActive = await _db.AlertRecordEntities
            .CountAsync(a => !a.Silenced, cancellationToken);

        var controls = new DashboardControls(
            await _controls.IsEnabledAsync(LabControlKeys.Consumer, cancellationToken),
            await _controls.IsEnabledAsync(LabControlKeys.Publisher, cancellationToken),
            await _controls.IsEnabledAsync(LabControlKeys.Failure, cancellationToken));

        var source = new DashboardSource(
            _env.Database,
            _env.RedisConfigured ? "configured" : "not_configured",
            !_env.RabbitConfigured
                ? "not_configured"
                : depth.Available ? "connected" : "unavailable");

        return new DashboardSnapshot(
            DateTime.UtcNow, todayDto, inventory, queues, outboxPending, deadLetterPending,
            alertsActive, controls, source);
    }
}
