namespace Pulse.Infrastructure.Alerts;

/// <summary>
/// M5 5.3：告警去重与静默的状态端口。
/// Redis 实现用独立命名空间 <c>alert:seen:{key}</c>（SETNX + 窗口 TTL）与
/// <c>alert:silence:{key}</c>（TTL 静默），**不改动** idem:/dedup: 两个既有键。
/// Redis 未配置时用内存实现降级，便于 SQLite 单测。
/// </summary>
public interface IAlertStateStore
{
    /// <summary>SETNX 语义：首次返回 true，窗口内重复返回 false。</summary>
    Task<bool> TryMarkSeenAsync(
        string alertKey,
        TimeSpan window,
        CancellationToken cancellationToken = default);

    Task SetSilenceAsync(
        string alertKey,
        DateTime untilUtc,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    /// <summary>静默截止时间；未静默或已过期返回 null。</summary>
    Task<DateTime?> GetSilencedUntilUtcAsync(
        string alertKey,
        CancellationToken cancellationToken = default);
}
