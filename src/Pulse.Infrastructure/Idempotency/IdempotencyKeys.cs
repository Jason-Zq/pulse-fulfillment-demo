namespace Pulse.Infrastructure.Idempotency;

/// <summary>Redis 幂等短钥匙的键约定（SPEC 第 5 节：idem:{token}、dedup:{event_id}）。</summary>
public static class IdempotencyKeys
{
    public const string TokenKeyPrefix = "idem:";

    /// <summary>消费端短钥匙的独立命名空间，与下单 token 的 idem: 平行。</summary>
    public const string EventKeyPrefix = "dedup:";

    /// <summary>短钥匙默认 TTL 24 小时。</summary>
    public static TimeSpan DefaultTtl => TimeSpan.FromHours(24);

    /// <summary>下单幂等 token：idem:{token}。</summary>
    public static string ForToken(string token) =>
        // 已带独立命名空间的完整键（消费端 dedup:）原样使用，避免 idem:dedup: 双前缀。
        token.StartsWith(EventKeyPrefix, StringComparison.Ordinal)
            ? token
            : $"{TokenKeyPrefix}{token}";

    /// <summary>消费端短钥匙：dedup:{event_id}。</summary>
    public static string ForEventId(string eventId) => $"{EventKeyPrefix}{eventId}";
}
