using System.Globalization;
using Pulse.Infrastructure.Messaging;
using StackExchange.Redis;

namespace Pulse.Infrastructure.Alerts;

/// <summary>
/// Redis 实现：独立命名空间 alert:seen:{key}（SETNX + 窗口 TTL）与
/// alert:silence:{key}（TTL 静默）。连接串来自 RedisOptions，构造懒连接、故障降级不抛给宿主。
/// 不触碰 idem:{token} / dedup:{event_id} 两个既有键（C 层双前缀缺陷已修复）。
/// </summary>
public sealed class RedisAlertStateStore : IAlertStateStore, IDisposable
{
    public const string SeenPrefix = "alert:seen:";
    public const string SilencePrefix = "alert:silence:";

    private readonly RedisOptions _options;
    private readonly object _sync = new();
    private ConnectionMultiplexer? _connection;
    private IDatabase? _database;

    public RedisAlertStateStore(RedisOptions options) => _options = options;

    public async Task<bool> TryMarkSeenAsync(
        string alertKey,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return false;
        }

        try
        {
            return await EnsureDatabase().StringSetAsync(
                SeenPrefix + alertKey, "1", window, when: When.NotExists);
        }
        catch (RedisException)
        {
            return false;
        }
    }

    public async Task SetSilenceAsync(
        string alertKey,
        DateTime untilUtc,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return;
        }

        try
        {
            await EnsureDatabase().StringSetAsync(
                SilencePrefix + alertKey,
                untilUtc.ToString("O", CultureInfo.InvariantCulture),
                ttl);
        }
        catch (RedisException)
        {
            // 静默写入失败不阻塞主链路（降级为未静默）。
        }
    }

    public async Task<DateTime?> GetSilencedUntilUtcAsync(
        string alertKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return null;
        }

        try
        {
            var value = await EnsureDatabase().StringGetAsync(SilencePrefix + alertKey);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            return DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var untilUtc)
                ? untilUtc
                : null;
        }
        catch (RedisException)
        {
            return null;
        }
    }

    private IDatabase EnsureDatabase()
    {
        lock (_sync)
        {
            if (_database is null)
            {
                _connection = ConnectionMultiplexer.Connect(_options.ConnectionString);
                _database = _connection.GetDatabase();
            }

            return _database;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _connection?.Dispose();
            _connection = null;
            _database = null;
        }
    }
}
