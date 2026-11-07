using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M3;

public class PersistenceEntitiesTests
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

        public PulseDbContext NewDbContext() => new(
            new DbContextOptionsBuilder<PulseDbContext>().UseSqlite(_connection).Options);

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    [Fact]
    public async Task Processed_event_event_id_is_unique()
    {
        using var f = new Fixture();
        f.Db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = "evt-1",
            ProcessedAtUtc = DateTime.UtcNow
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        using var db2 = f.NewDbContext();
        db2.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = "evt-1",
            ProcessedAtUtc = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Processed_event_round_trips_through_sqlite()
    {
        using var f = new Fixture();
        var at = new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);
        f.Db.ProcessedEvents.Add(new ProcessedEvent { EventId = "evt-x", ProcessedAtUtc = at });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var stored = await f.Db.ProcessedEvents.SingleAsync();
        Assert.Equal("evt-x", stored.EventId);
        Assert.Equal(at, stored.ProcessedAtUtc);
    }

    [Fact]
    public void Dead_letter_defaults_to_pending_and_exposes_all_fields()
    {
        var blank = new DeadLetter();
        Assert.Equal("pending", blank.Status);
        Assert.Equal(0, blank.RetryCount);
        Assert.Null(blank.HandledAtUtc);

        var at = new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);
        var full = new DeadLetter
        {
            OriginalEventId = "evt-1",
            Payload = "{\"orderNo\":\"PO-1\"}",
            Error = "boom",
            RetryCount = 3,
            Status = "handled",
            CreatedAtUtc = at,
            HandledAtUtc = at
        };

        Assert.Equal("evt-1", full.OriginalEventId);
        Assert.Equal("{\"orderNo\":\"PO-1\"}", full.Payload);
        Assert.Equal("boom", full.Error);
        Assert.Equal(3, full.RetryCount);
        Assert.Equal("handled", full.Status);
        Assert.Equal(at, full.CreatedAtUtc);
        Assert.Equal(at, full.HandledAtUtc);
    }

    [Fact]
    public async Task Dead_letter_persists_with_default_pending_status()
    {
        using var f = new Fixture();
        f.Db.DeadLetters.Add(new DeadLetter
        {
            OriginalEventId = "evt-1",
            Payload = "{}",
            Error = "err",
            RetryCount = 3,
            CreatedAtUtc = DateTime.UtcNow
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        Assert.Equal(1, await f.Db.DeadLetters.CountAsync());
        Assert.Equal("pending", (await f.Db.DeadLetters.SingleAsync()).Status);
    }
}
