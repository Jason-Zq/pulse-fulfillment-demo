using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M3;

public class DeadLetterServiceTests
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

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    private static DeadLetterRecord Sample() => new(
        "evt-1",
        "{\"orderNo\":\"PO-1\"}",
        "boom",
        3);

    [Fact]
    public void Decide_below_three_retries_returns_retry_with_order_retry_key()
    {
        var decider = new RetryDecider();

        Assert.Equal(RetryDecision.Retry, decider.Decide(0));
        Assert.Equal(RetryDecision.Retry, decider.Decide(2));
        Assert.Equal(OrderTopology.RoutingKeyFulfillmentRetry,
            decider.RoutingKeyFor(RetryDecision.Retry));
    }

    [Fact]
    public void Decide_at_three_or_more_returns_dead_letter_with_order_dead_key()
    {
        var decider = new RetryDecider();

        Assert.Equal(RetryDecision.DeadLetter, decider.Decide(3));
        Assert.Equal(RetryDecision.DeadLetter, decider.Decide(10));
        Assert.Equal(OrderTopology.RoutingKeyDeadLetter,
            decider.RoutingKeyFor(RetryDecision.DeadLetter));
    }

    [Fact]
    public async Task Record_persists_all_fields_with_pending_status()
    {
        using var f = new Fixture();

        await new DeadLetterService(f.Db).RecordAsync(Sample());

        var letter = await f.Db.DeadLetters.SingleAsync();
        Assert.Equal("evt-1", letter.OriginalEventId);
        Assert.Equal("{\"orderNo\":\"PO-1\"}", letter.Payload);
        Assert.Equal("boom", letter.Error);
        Assert.Equal(3, letter.RetryCount);
        Assert.Equal("pending", letter.Status);
        Assert.Null(letter.HandledAtUtc);
    }

    [Fact]
    public async Task Retry_marks_handled_and_writes_one_pending_outbox()
    {
        using var f = new Fixture();
        var sut = new DeadLetterService(f.Db);
        var letter = await sut.RecordAsync(Sample());

        var ok = await sut.RetryAsync(letter.Id);

        Assert.True(ok);
        var stored = await f.Db.DeadLetters.SingleAsync();
        Assert.Equal("handled", stored.Status);
        Assert.NotNull(stored.HandledAtUtc);
        var message = await f.Db.OutboxMessages.SingleAsync();
        Assert.Equal("pending", message.Status);
        Assert.False(string.IsNullOrWhiteSpace(message.EventId));
        Assert.Equal(OrderTopology.RoutingKeyOrderPaid, message.RoutingKey);
    }

    [Fact]
    public async Task Retry_of_already_handled_does_not_write_another_outbox()
    {
        using var f = new Fixture();
        var sut = new DeadLetterService(f.Db);
        var letter = await sut.RecordAsync(Sample());

        await sut.RetryAsync(letter.Id);
        var second = await sut.RetryAsync(letter.Id);

        Assert.True(second);
        Assert.Equal(1, await f.Db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Retry_of_missing_id_returns_false()
    {
        using var f = new Fixture();

        Assert.False(await new DeadLetterService(f.Db).RetryAsync(999));
        Assert.Equal(0, await f.Db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task List_can_filter_by_status()
    {
        using var f = new Fixture();
        var sut = new DeadLetterService(f.Db);
        var first = await sut.RecordAsync(Sample());
        var second = await sut.RecordAsync(Sample() with { OriginalEventId = "evt-2" });
        await sut.RetryAsync(second.Id);

        var pending = await sut.ListAsync("pending");
        var all = await sut.ListAsync(null);

        Assert.Single(pending);
        Assert.Equal(first.Id, pending[0].Id);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void Retry_services_resolve_from_di_scope()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddSingleton(_ =>
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            return connection;
        });
        services.AddDbContext<PulseDbContext>((sp, options) =>
            options.UseSqlite(sp.GetRequiredService<SqliteConnection>()));
        services.AddPulseMessaging(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<RetryDecider>(scope.ServiceProvider.GetRequiredService<IRetryDecider>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DeadLetterService>());
    }
}
