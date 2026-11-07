using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Alerts;

public sealed record AlertItem(
    long Id,
    string AlertKey,
    string Level,
    string Message,
    DateTime CreatedAtUtc,
    bool Silenced,
    DateTime? SilencedUntilUtc);

public sealed record SilenceResult(string AlertKey, bool Silenced, DateTime? SilencedUntilUtc);

/// <summary>
/// M5 5.3：扫描 3 类条件生成告警——死信 pending&gt;0、available&lt;lowStockThreshold、队列积压。
/// Redis alert:seen:{key} SETNX 去重窗口、alert:silence:{key} TTL 静默；
/// Redis 未配置时由 <see cref="InMemoryAlertStateStore"/> 降级。
/// GET /api/alerts 先 RefreshAsync 再返回；POST silence 写静默键并标 Silenced。
/// </summary>
public sealed class AlertService
{
    public const int LowStockThreshold = 10;
    public const int QueueBacklogThreshold = 10;
    public static readonly TimeSpan SeenWindow = TimeSpan.FromHours(1);

    private const string KeyDeadLetterPending = "dead_letter_pending";
    private const string LowStockPrefix = "low_stock:";
    private const string QueueBacklogPrefix = "queue_backlog:";

    private readonly PulseDbContext _db;
    private readonly IAlertStateStore _state;
    private readonly IQueueDepthProvider _queues;
    private readonly TimeProvider _clock;

    public AlertService(
        PulseDbContext db,
        IAlertStateStore state,
        IQueueDepthProvider queues,
        TimeProvider? clock = null)
    {
        _db = db;
        _state = state;
        _queues = queues;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>扫描并落地新告警，返回新建行数；去重窗口内重复条件不再落行。</summary>
    public async Task<int> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var candidates = new List<(string Key, string Level, string Message)>();

        // 条件 1：死信待处理 > 0。
        var deadLetterPending = await _db.DeadLetters.AsNoTracking()
            .CountAsync(d => d.Status == "pending", cancellationToken);
        if (deadLetterPending > 0)
        {
            candidates.Add((KeyDeadLetterPending, "critical",
                $"有 {deadLetterPending} 条死信待处理"));
        }

        // 条件 2：available < lowStockThreshold（默认 10，与 dashboard 一致）。
        var lowStock = await _db.Inventories.AsNoTracking()
            .Where(i => i.Available < LowStockThreshold)
            .Select(i => new { i.Sku, i.Available })
            .ToListAsync(cancellationToken);
        foreach (var item in lowStock)
        {
            candidates.Add((LowStockPrefix + item.Sku, "warning",
                $"{item.Sku} 可用库存 {item.Available} 低于阈值 {LowStockThreshold}"));
        }

        // 条件 3：队列积压（仅深度可用时，不伪造 0）。
        var depth = await _queues.GetAsync(cancellationToken);
        if (depth.Available)
        {
            AddIfBacklog(candidates, "fulfillment", depth.Fulfillment);
            AddIfBacklog(candidates, "orderTimeout", depth.OrderTimeout);
            AddIfBacklog(candidates, "fulfillmentRetry", depth.FulfillmentRetry);
            AddIfBacklog(candidates, "dlq", depth.Dlq);
        }

        var existing = await _db.AlertRecordEntities.ToListAsync(cancellationToken);
        var newRows = new List<AlertRecordEntity>();
        foreach (var (key, level, message) in candidates)
        {
            if (!await _state.TryMarkSeenAsync(key, SeenWindow, cancellationToken))
            {
                continue;
            }

            var row = new AlertRecordEntity
            {
                AlertKey = key,
                Level = level,
                Message = message,
                CreatedAtUtc = now
            };
            _db.AlertRecordEntities.Add(row);
            newRows.Add(row);
        }

        // 同步静默快照：TTL 到期后自动清除 Silenced / SilencedUntilUtc。
        var changed = false;
        foreach (var row in existing.Concat(newRows))
        {
            var until = await _state.GetSilencedUntilUtcAsync(row.AlertKey, cancellationToken);
            var silenced = until is not null && until > now;
            var snapshot = silenced ? until : null;
            if (row.Silenced != silenced || row.SilencedUntilUtc != snapshot)
            {
                row.Silenced = silenced;
                row.SilencedUntilUtc = snapshot;
                changed = true;
            }
        }

        if (newRows.Count > 0 || changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return newRows.Count;
    }

    public async Task<IReadOnlyList<AlertItem>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken);
        var take = limit < 1 ? 50 : limit;
        var rows = await _db.AlertRecordEntities.AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(r => new AlertItem(
            r.Id, r.AlertKey, r.Level, r.Message, r.CreatedAtUtc,
            r.Silenced, r.SilencedUntilUtc)).ToList();
    }

    public async Task<SilenceResult?> SilenceAsync(
        string alertKey,
        int minutes,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.AlertRecordEntities
            .FirstOrDefaultAsync(a => a.AlertKey == alertKey, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var until = now.AddMinutes(minutes);
        await _state.SetSilenceAsync(alertKey, until, TimeSpan.FromMinutes(minutes), cancellationToken);
        row.Silenced = true;
        row.SilencedUntilUtc = until;
        await _db.SaveChangesAsync(cancellationToken);
        return new SilenceResult(alertKey, true, until);
    }

    private static void AddIfBacklog(
        List<(string Key, string Level, string Message)> candidates,
        string queueName,
        long depth)
    {
        if (depth > QueueBacklogThreshold)
        {
            candidates.Add((QueueBacklogPrefix + queueName, "warning",
                $"队列 {queueName} 积压 {depth} 条（阈值 {QueueBacklogThreshold}）"));
        }
    }
}
