using Pulse.Infrastructure.Messaging;
using StackExchange.Redis;

namespace Pulse.Infrastructure.Idempotency;

/// <summary>
/// Redis 幂等短钥匙（SPEC 第 5 节）：SETNX（When.NotExists）+ TTL，键为 idem:{token}。
/// 连接串一律来自 RedisOptions（配置），构造时不连 Redis（懒连接），
/// 未配置或连接失败时返回 false 而非抛异常，避免阻塞下单入口。
/// </summary>
public sealed class RedisIdempotencyGuard : IIdempotencyGuard, IDisposable
{
    private readonly RedisOptions _options;
    private readonly object _sync = new();
    private ConnectionMultiplexer? _connection;
    private IDatabase? _database;

    public RedisIdempotencyGuard(RedisOptions options) => _options = options;

    public async Task<bool> TryAcquireAsync(
        string token,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("缺少幂等 token", nameof(token));
        }

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return false;
        }

        try
        {
            var database = EnsureDatabase();
            return await database.StringSetAsync(
                IdempotencyKeys.ForToken(token),
                "1",
                ttl ?? IdempotencyKeys.DefaultTtl,
                when: When.NotExists);
        }
        catch (RedisException)
        {
            return false;
        }
    }

    public async Task ReleaseAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return;
        }

        try
        {
            // 删除失败不阻塞主链路：短钥匙本身带 TTL，最坏情况是本次重试被判重。
            await EnsureDatabase().KeyDeleteAsync(IdempotencyKeys.ForToken(token));
        }
        catch (RedisException)
        {
            // 与 TryAcquireAsync 一致：Redis 不可用时降级，不抛给消费者宿主。
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
