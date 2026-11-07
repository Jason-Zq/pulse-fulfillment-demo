using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Idempotency;

/// <inheritdoc cref="IEventDeduplicator"/>
public sealed class EventDeduplicator : IEventDeduplicator
{
    public const string DedupKeyPrefix = IdempotencyKeys.EventKeyPrefix;

    /// <summary>短钥匙 TTL 24 小时（SPEC 第 5 节 dedup:{event_id}）。</summary>
    public static readonly TimeSpan DedupTtl = TimeSpan.FromHours(24);

    private readonly PulseDbContext _db;
    private readonly IIdempotencyGuard _guard;

    public EventDeduplicator(PulseDbContext db, IIdempotencyGuard guard)
    {
        _db = db;
        _guard = guard;
    }

    // 键名前缀统一由 IdempotencyKeys 管理：ForToken 认得 dedup: 命名空间，
    // 不会再套一层 idem:（C 层 Docker 联调发现的双前缀回归）。
    public static string KeyFor(string eventId) => IdempotencyKeys.ForEventId(eventId);

    public async Task<DedupDecision> TryBeginAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new ArgumentException("缺少 eventId", nameof(eventId));
        }

        // 唯一键最终保障：短钥匙过期后只要 processed_events 有记录，仍判重（无需再查 Redis）。
        var alreadyProcessed = await _db.ProcessedEvents.AsNoTracking()
            .AnyAsync(x => x.EventId == eventId, cancellationToken);
        if (alreadyProcessed)
        {
            return DedupDecision.Duplicate;
        }

        var acquired = await _guard.TryAcquireAsync(KeyFor(eventId), DedupTtl, cancellationToken);
        return acquired ? DedupDecision.Proceed : DedupDecision.Duplicate;
    }

    public async Task MarkProcessedAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        _db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = eventId,
            ProcessedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task ReleaseAsync(
        string eventId,
        CancellationToken cancellationToken = default) =>
        _guard.ReleaseAsync(KeyFor(eventId), cancellationToken);
}
