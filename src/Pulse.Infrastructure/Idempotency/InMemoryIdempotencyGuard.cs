namespace Pulse.Infrastructure.Idempotency;

/// <summary>
/// 基于进程内字典 + 可注入时钟的 SETNX 实现，用于无 Redis 的本地演示与单元测试。
/// 生产语义以 <see cref="RedisIdempotencyGuard"/> 为准。
/// </summary>
public sealed class InMemoryIdempotencyGuard : IIdempotencyGuard
{
    private readonly TimeProvider _clock;
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTimeOffset> _expiresAt = new();

    public InMemoryIdempotencyGuard(TimeProvider? clock = null) =>
        _clock = clock ?? TimeProvider.System;

    /// <summary>当前持有的键，供测试断言 idem: 前缀。</summary>
    public IReadOnlyCollection<string> Keys
    {
        get
        {
            lock (_sync)
            {
                return _expiresAt.Keys.ToArray();
            }
        }
    }

    public Task<bool> TryAcquireAsync(
        string token,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("缺少幂等 token", nameof(token));
        }

        var key = IdempotencyKeys.ForToken(token);
        var now = _clock.GetUtcNow();
        var expiresAt = now + (ttl ?? IdempotencyKeys.DefaultTtl);

        lock (_sync)
        {
            if (_expiresAt.TryGetValue(key, out var existing) && existing > now)
            {
                return Task.FromResult(false);
            }

            _expiresAt[key] = expiresAt;
            return Task.FromResult(true);
        }
    }

    public Task ReleaseAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var key = IdempotencyKeys.ForToken(token);
        lock (_sync)
        {
            _expiresAt.Remove(key);
        }

        return Task.CompletedTask;
    }
}
