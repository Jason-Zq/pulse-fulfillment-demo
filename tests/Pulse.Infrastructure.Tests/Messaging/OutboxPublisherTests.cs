using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.Messaging;

public class OutboxPublisherTests
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

        public void Seed(
            string eventId,
            string status = "pending",
            int retryCount = 0,
            int createdOffsetSeconds = 0)
        {
            Db.OutboxMessages.Add(new OutboxMessage
            {
                EventId = eventId,
                RoutingKey = "order.created",
                Payload = "{\"orderNo\":\"PO-1\"}",
                Status = status,
                RetryCount = retryCount,
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(createdOffsetSeconds)
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

    private sealed class FakeMessagePublisher : IMessagePublisher
    {
        public List<PublishRequest> Requests { get; } = new();

        public Func<PublishRequest, PublishResult> Handler { get; set; } =
            _ => new PublishResult(true);

        public Task<PublishResult> PublishAsync(
            PublishRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Handler(request));
        }
    }

    private static OutboxPublisher CreateSut(Fixture f, FakeMessagePublisher fake) =>
        new(f.Db, fake);

    [Fact]
    public async Task Pending_message_is_published_and_marked_published()
    {
        using var f = new Fixture();
        f.Seed("evt-1");
        var fake = new FakeMessagePublisher();

        var summary = await CreateSut(f, fake).PublishPendingAsync(10);

        Assert.Equal(1, summary.Attempted);
        Assert.Equal(1, summary.Published);
        var request = Assert.Single(fake.Requests);
        Assert.Equal("evt-1", request.EventId);
        Assert.Equal("order.created", request.RoutingKey);
        Assert.Equal("{\"orderNo\":\"PO-1\"}", request.Payload);

        var message = await f.Db.OutboxMessages.SingleAsync();
        Assert.Equal("published", message.Status);
        Assert.NotNull(message.PublishedAtUtc);
    }

    [Fact]
    public async Task Confirm_failure_increments_retry_and_keeps_message_pending()
    {
        using var f = new Fixture();
        f.Seed("evt-1");
        var fake = new FakeMessagePublisher { Handler = _ => new PublishResult(false, "nack") };

        var summary = await CreateSut(f, fake).PublishPendingAsync(10);

        Assert.Equal(1, summary.Retried);
        var message = await f.Db.OutboxMessages.SingleAsync();
        Assert.Equal("pending", message.Status);
        Assert.Equal(1, message.RetryCount);
        Assert.Null(message.PublishedAtUtc);
    }

    [Fact]
    public async Task Publisher_exception_is_absorbed_and_counted_as_retry()
    {
        using var f = new Fixture();
        f.Seed("evt-1");
        var fake = new FakeMessagePublisher
        {
            Handler = _ => throw new InvalidOperationException("broker down")
        };

        var summary = await CreateSut(f, fake).PublishPendingAsync(10);

        Assert.Equal(1, summary.Retried);
        var message = await f.Db.OutboxMessages.SingleAsync();
        Assert.Equal("pending", message.Status);
        Assert.Equal(1, message.RetryCount);
    }

    [Fact]
    public async Task Reaching_max_attempts_marks_message_failed()
    {
        using var f = new Fixture();
        f.Seed("evt-1", retryCount: OutboxPublisher.MaxPublishAttempts - 1);
        var fake = new FakeMessagePublisher { Handler = _ => new PublishResult(false) };

        var summary = await CreateSut(f, fake).PublishPendingAsync(10);

        Assert.Equal(1, summary.Failed);
        var message = await f.Db.OutboxMessages.SingleAsync();
        Assert.Equal("failed", message.Status);
        Assert.Equal(OutboxPublisher.MaxPublishAttempts, message.RetryCount);
    }

    [Fact]
    public async Task Already_published_messages_are_not_republished()
    {
        using var f = new Fixture();
        f.Seed("evt-1", status: "published");
        f.Seed("evt-2");
        var fake = new FakeMessagePublisher();

        var first = await CreateSut(f, fake).PublishPendingAsync(10);
        var second = await CreateSut(f, fake).PublishPendingAsync(10);

        Assert.Equal(1, first.Attempted);
        Assert.Equal(0, second.Attempted);
        Assert.DoesNotContain(fake.Requests, r => r.EventId == "evt-1");
    }

    [Fact]
    public async Task Batch_is_limited_and_processed_in_created_at_order()
    {
        using var f = new Fixture();
        f.Seed("evt-late", createdOffsetSeconds: 30);
        f.Seed("evt-early", createdOffsetSeconds: 10);
        f.Seed("evt-mid", createdOffsetSeconds: 20);
        var fake = new FakeMessagePublisher();

        var summary = await CreateSut(f, fake).PublishPendingAsync(2);

        Assert.Equal(2, summary.Attempted);
        Assert.Equal(new[] { "evt-early", "evt-mid" }, fake.Requests.Select(r => r.EventId).ToArray());
        var late = await f.Db.OutboxMessages.SingleAsync(m => m.EventId == "evt-late");
        Assert.Equal("pending", late.Status);
    }
}
