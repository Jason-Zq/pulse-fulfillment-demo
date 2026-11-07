using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Alerts;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M5;

/// <summary>
/// M5 5.3：告警去重（alert:seen SETNX 语义）与静默（alert:silence TTL），
/// 以及 AlertService 扫描 3 类条件。Redis 未配置时用内存实现降级（可注入时钟）。
/// </summary>
public class AlertServiceTests
{
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan delta) => Now += delta;
    }

    private sealed class FakeQueues(QueueDepthSnapshot snapshot) : IQueueDepthProvider
    {
        public Task<QueueDepthSnapshot> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection;

        public PulseDbContext Db { get; }
        public FakeClock Clock { get; } = new();
        public InMemoryAlertStateStore State { get; }

        public Fixture()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            Db = new PulseDbContext(
                new DbContextOptionsBuilder<PulseDbContext>().UseSqlite(_connection).Options);
            Db.Database.EnsureCreated();
            State = new InMemoryAlertStateStore(Clock);
        }

        public AlertService Service(QueueDepthSnapshot? snapshot = null) =>
            new(Db, State, new FakeQueues(snapshot ?? QueueDepthSnapshot.Unavailable()), Clock);

        public void SeedDeadLetterPending()
        {
            Db.DeadLetters.Add(new DeadLetter
            {
                OriginalEventId = $"evt-{Guid.NewGuid():N}",
                Payload = "{}",
                Error = "boom",
                RetryCount = 3,
                Status = "pending",
                CreatedAtUtc = Clock.GetUtcNow().UtcDateTime
            });
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
        }

        public void SeedInventory(string sku, int available)
        {
            Db.Inventories.Add(new Inventory { Sku = sku, Available = available, Reserved = 0, Version = 0 });
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    [Fact]
    public async Task Seen_key_is_setnx_and_expires_after_window()
    {
        var f = new Fixture();
        // 首次 true，同 key 窗口内再次 false（去重）。
        Assert.True(await f.State.TryMarkSeenAsync("dead_letter_pending", TimeSpan.FromMinutes(5)));
        Assert.False(await f.State.TryMarkSeenAsync("dead_letter_pending", TimeSpan.FromMinutes(5)));
        Assert.True(await f.State.TryMarkSeenAsync("another_condition", TimeSpan.FromMinutes(5)));

        // 窗口过期后可再次获取（SETNX + TTL 语义）。
        f.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.True(await f.State.TryMarkSeenAsync("dead_letter_pending", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task Silence_key_has_ttl_and_reports_until_utc()
    {
        var f = new Fixture();
        var until = f.Clock.GetUtcNow().UtcDateTime.AddMinutes(30);
        await f.State.SetSilenceAsync("low_stock:SKU-1", until, TimeSpan.FromMinutes(30));

        Assert.Equal(until, await f.State.GetSilencedUntilUtcAsync("low_stock:SKU-1"));

        f.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Null(await f.State.GetSilencedUntilUtcAsync("low_stock:SKU-1"));
    }

    [Fact]
    public async Task Refresh_creates_one_row_per_condition_and_dedups_on_repeat()
    {
        using var f = new Fixture();
        f.SeedDeadLetterPending();
        f.SeedInventory("SKU-1", 5);      // < 默认阈值 10 → 低库存告警
        f.SeedInventory("SKU-2", 50);     // 正常，不告警

        var svc = f.Service();
        var first = await svc.RefreshAsync();
        var second = await svc.RefreshAsync();

        Assert.Equal(2, first);
        Assert.Equal(0, second);          // 去重窗口内不重复落行
        Assert.Equal(2, await f.Db.AlertRecordEntities.CountAsync());
        var keys = await f.Db.AlertRecordEntities.Select(a => a.AlertKey).ToListAsync();
        Assert.Contains("dead_letter_pending", keys);
        Assert.Contains("low_stock:SKU-1", keys);
    }

    [Fact]
    public async Task Silence_marks_existing_alert_and_List_reflects_it()
    {
        using var f = new Fixture();
        f.SeedDeadLetterPending();
        var svc = f.Service();
        await svc.RefreshAsync();

        var result = await svc.SilenceAsync("dead_letter_pending", 30);

        Assert.NotNull(result);
        Assert.Equal("dead_letter_pending", result!.AlertKey);
        Assert.True(result.Silenced);
        Assert.NotNull(result.SilencedUntilUtc);

        var items = await svc.ListAsync(50);
        var alert = Assert.Single(items);
        Assert.True(alert.Silenced);
        Assert.Equal("dead_letter_pending", alert.AlertKey);
        Assert.Equal("critical", alert.Level);
    }

    [Fact]
    public async Task Silence_missing_alert_returns_null()
    {
        using var f = new Fixture();
        var result = await f.Service().SilenceAsync("does-not-exist", 30);
        Assert.Null(result);
    }

    [Fact]
    public async Task Queue_backlog_alert_only_when_depth_available_and_over_threshold()
    {
        using var f = new Fixture();
        var backlog = new QueueDepthSnapshot(0, 0, 25, 0, Available: true);
        var svc = f.Service(backlog);

        var created = await svc.RefreshAsync();

        Assert.Equal(1, created);
        var keys = await f.Db.AlertRecordEntities.Select(a => a.AlertKey).ToListAsync();
        Assert.Contains("queue_backlog:fulfillmentRetry", keys);
    }
}
