namespace Pulse.Infrastructure.Alerts;

/// <summary>
/// 内存降级实现（Redis 未配置 / 单测）：用可注入时钟复刻 SETNX + TTL 语义。
/// </summary>
public sealed class InMemoryAlertStateStore : IAlertStateStore
{
    private readonly TimeProvider _clock;
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTimeOffset> _seen = new();
    private readonly Dictionary<string, (DateTime UntilUtc, DateTimeOffset ExpiresAt)> _silence = new();

    public InMemoryAlertStateStore(TimeProvider clock) => _clock = clock;

    public Task<bool> TryMarkSeenAsync(
        string alertKey,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        lock (_sync)
        {
            if (_seen.TryGetValue(alertKey, out var existing) && existing > now)
            {
                return Task.FromResult(false);
            }

            _seen[alertKey] = now + window;
            return Task.FromResult(true);
        }
    }

    public Task SetSilenceAsync(
        string alertKey,
        DateTime untilUtc,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _silence[alertKey] = (untilUtc, _clock.GetUtcNow() + ttl);
        }

        return Task.CompletedTask;
    }

    public Task<DateTime?> GetSilencedUntilUtcAsync(
        string alertKey,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        lock (_sync)
        {
            if (_silence.TryGetValue(alertKey, out var entry) && entry.ExpiresAt > now)
            {
                return Task.FromResult<DateTime?>(entry.UntilUtc);
            }

            return Task.FromResult<DateTime?>(null);
        }
    }
}
