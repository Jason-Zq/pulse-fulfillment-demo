using Pulse.Infrastructure.Idempotency;
using Xunit;

namespace Pulse.Infrastructure.Tests.Messaging;

public class IdempotencyGuardTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    [Fact]
    public async Task First_acquire_succeeds_and_duplicate_is_rejected()
    {
        var guard = new InMemoryIdempotencyGuard();

        Assert.True(await guard.TryAcquireAsync("tok-1"));
        Assert.False(await guard.TryAcquireAsync("tok-1"));
    }

    [Fact]
    public async Task Different_tokens_are_independent()
    {
        var guard = new InMemoryIdempotencyGuard();

        Assert.True(await guard.TryAcquireAsync("tok-a"));
        Assert.True(await guard.TryAcquireAsync("tok-b"));
    }

    [Fact]
    public async Task Expired_ttl_allows_reacquire()
    {
        var clock = new ManualTimeProvider();
        var guard = new InMemoryIdempotencyGuard(clock);

        Assert.True(await guard.TryAcquireAsync("tok-1", TimeSpan.FromHours(1)));
        clock.UtcNow = clock.UtcNow.AddHours(2);

        Assert.True(await guard.TryAcquireAsync("tok-1", TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Token_key_uses_idem_prefix()
    {
        Assert.Equal("idem:tok-1", IdempotencyKeys.ForToken("tok-1"));
    }

    [Fact]
    public async Task In_memory_guard_stores_keys_with_idem_prefix()
    {
        var guard = new InMemoryIdempotencyGuard();

        await guard.TryAcquireAsync("tok-1");

        Assert.Contains("idem:tok-1", guard.Keys);
    }

    [Fact]
    public void Default_ttl_is_24_hours()
    {
        Assert.Equal(TimeSpan.FromHours(24), IdempotencyKeys.DefaultTtl);
    }

    /// <summary>
    /// C 层 Docker 联调回归：消费端短钥匙经过 RedisIdempotencyGuard 后必须是
    /// SPEC 第 5 节的 dedup:{event_id}，不能被再套一层 idem: 变成 idem:dedup:...。
    /// </summary>
    [Fact]
    public void Event_dedup_key_is_not_double_prefixed_by_the_guard()
    {
        var token = EventDeduplicator.KeyFor("evt-1");

        Assert.Equal("dedup:evt-1", IdempotencyKeys.ForToken(token));
    }

    [Fact]
    public async Task Blank_token_is_rejected()
    {
        var guard = new InMemoryIdempotencyGuard();

        await Assert.ThrowsAsync<ArgumentException>(() => guard.TryAcquireAsync("   "));
    }
}
