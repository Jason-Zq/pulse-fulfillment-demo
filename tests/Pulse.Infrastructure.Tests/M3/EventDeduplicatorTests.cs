using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Idempotency;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M3;

public class EventDeduplicatorTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection;

        public PulseDbContext Db { get; }

        public Fixture()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            Db = new PulseDbContext(
                new DbContextOptionsBuilder<PulseDbContext>().UseSqlite(_connection).Options);
            Db.Database.EnsureCreated();
        }

        public void SeedInventory()
        {
            Db.Inventories.Add(new Inventory
            {
                Sku = "SKU-1",
                Available = 10,
                Reserved = 0,
                Version = 0
            });
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    private sealed class RecordingGuard : IIdempotencyGuard
    {
        public int Calls { get; private set; }
        public string? LastToken { get; private set; }
        public TimeSpan? LastTtl { get; private set; }
        public bool Result { get; set; } = true;

        public string? LastReleasedToken { get; private set; }

        public Task<bool> TryAcquireAsync(
            string token,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastToken = token;
            LastTtl = ttl;
            return Task.FromResult(Result);
        }

        public Task ReleaseAsync(
            string token,
            CancellationToken cancellationToken = default)
        {
            LastReleasedToken = token;
            return Task.CompletedTask;
        }
    }

    private static EventDeduplicator CreateSut(Fixture f, IIdempotencyGuard guard) =>
        new(f.Db, guard);

    [Fact]
    public async Task First_event_proceeds_and_is_marked_processed()
    {
        using var f = new Fixture();
        var sut = CreateSut(f, new InMemoryIdempotencyGuard());

        Assert.Equal(DedupDecision.Proceed, await sut.TryBeginAsync("evt-1"));
        await sut.MarkProcessedAsync("evt-1");

        var stored = await f.Db.ProcessedEvents.SingleAsync();
        Assert.Equal("evt-1", stored.EventId);
        Assert.NotEqual(default, stored.ProcessedAtUtc);
    }

    [Fact]
    public async Task Duplicate_event_is_rejected_and_business_handler_runs_once()
    {
        using var f = new Fixture();
        var sut = CreateSut(f, new InMemoryIdempotencyGuard());
        var calls = 0;

        async Task Deliver(string eventId)
        {
            if (await sut.TryBeginAsync(eventId) == DedupDecision.Proceed)
            {
                calls++;
                await sut.MarkProcessedAsync(eventId);
            }
        }

        await Deliver("evt-1");
        var second = await sut.TryBeginAsync("evt-1");
        await Deliver("evt-1");

        Assert.Equal(1, calls);
        Assert.Equal(DedupDecision.Duplicate, second);
        Assert.Equal(1, await f.Db.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Db_record_is_final_guard_even_when_short_key_is_missing()
    {
        using var f = new Fixture();
        f.Db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = "evt-1",
            ProcessedAtUtc = DateTime.UtcNow
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var guard = new RecordingGuard();
        var sut = CreateSut(f, guard);

        Assert.Equal(DedupDecision.Duplicate, await sut.TryBeginAsync("evt-1"));
        Assert.Equal(0, guard.Calls);
    }

    [Fact]
    public async Task Dedup_key_uses_dedup_prefix_and_ttl_is_24h()
    {
        using var f = new Fixture();
        var guard = new RecordingGuard();
        var sut = CreateSut(f, guard);

        await sut.TryBeginAsync("evt-1");

        Assert.Equal("dedup:evt-1", EventDeduplicator.KeyFor("evt-1"));
        Assert.Equal("dedup:evt-1", guard.LastToken);
        Assert.Equal(TimeSpan.FromHours(24), guard.LastTtl);
        Assert.Equal(TimeSpan.FromHours(24), EventDeduplicator.DedupTtl);
    }

    [Fact]
    public async Task Short_key_rejection_is_reported_as_duplicate()
    {
        using var f = new Fixture();
        var sut = CreateSut(f, new RecordingGuard { Result = false });

        Assert.Equal(DedupDecision.Duplicate, await sut.TryBeginAsync("evt-1"));
    }

    [Fact]
    public async Task Mark_processed_commits_business_changes_in_the_same_save()
    {
        using var f = new Fixture();
        f.SeedInventory();
        var sut = CreateSut(f, new InMemoryIdempotencyGuard());

        var inventory = await f.Db.Inventories.SingleAsync();
        inventory.Available = 42;
        await sut.MarkProcessedAsync("evt-1");
        f.Db.ChangeTracker.Clear();

        Assert.Equal(42, (await f.Db.Inventories.SingleAsync()).Available);
        Assert.Equal(1, await f.Db.ProcessedEvents.CountAsync());
    }
}
