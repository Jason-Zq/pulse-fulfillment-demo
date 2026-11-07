using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Dashboard;
using Pulse.Infrastructure.Lab;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;
using Pulse.Infrastructure.Reports;
using Xunit;

namespace Pulse.Infrastructure.Tests.M5;

/// <summary>
/// M5 5.5：DashboardService 聚合今日订单 / GMV / 库存三态 / outbox / 死信 /
/// 活跃告警 / 控制开关；队列深度不可用时不伪造 0。
/// </summary>
public class DashboardServiceTests
{
    private sealed class FakeQueues(QueueDepthSnapshot snapshot) : IQueueDepthProvider
    {
        public Task<QueueDepthSnapshot> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(snapshot);
    }

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

        public DashboardService Service(
            QueueDepthSnapshot? snapshot = null,
            DashboardEnvironment? env = null) =>
            new(Db,
                new DailyReportService(Db),
                new FakeQueues(snapshot ?? QueueDepthSnapshot.Unavailable()),
                new EfLabControlStore(Db),
                env ?? new DashboardEnvironment("sqlite", false, false));

        public void Seed()
        {
            var today = DateTime.UtcNow;
            Db.Inventories.AddRange(
                new Inventory { Sku = "SKU-1", Available = 100, Reserved = 0, Version = 0 },
                new Inventory { Sku = "SKU-2", Available = 5, Reserved = 0, Version = 0 }); // 5 < 10 → 低库存
            Db.Orders.AddRange(
                MakeOrder("D-A", OrderStatus.Paid, 20m, today),
                MakeOrder("D-B", OrderStatus.Cancelled, 10m, today));
            Db.OutboxMessages.AddRange(
                MakeOutbox("o1"), MakeOutbox("o2"));
            Db.DeadLetters.Add(new DeadLetter
            {
                OriginalEventId = "dl-1", Payload = "{}", Error = "boom", RetryCount = 3,
                Status = "pending", CreatedAtUtc = today
            });
            Db.AlertRecordEntities.AddRange(
                new AlertRecordEntity { AlertKey = "active-1", Level = "warning", Message = "a", CreatedAtUtc = today, Silenced = false },
                new AlertRecordEntity { AlertKey = "silenced-1", Level = "info", Message = "b", CreatedAtUtc = today, Silenced = true });
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
        }

        private static Order MakeOrder(string no, OrderStatus status, decimal amount, DateTime created) => new()
        {
            OrderNo = no,
            IdempotencyKey = $"idem-{no}",
            TotalAmount = amount,
            Status = status,
            CreatedAtUtc = created,
            PaidAtUtc = status == OrderStatus.Created ? null : created,
            ExpiresAtUtc = created.AddMinutes(15),
            Version = 0
        };

        private static OutboxMessage MakeOutbox(string eventId) => new()
        {
            EventId = eventId, RoutingKey = "order.created", Payload = "{}",
            Status = "pending", RetryCount = 0, CreatedAtUtc = DateTime.UtcNow
        };

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    [Fact]
    public async Task Aggregates_all_metrics_and_marks_unavailable_queues_as_minus_one()
    {
        using var f = new Fixture();
        f.Seed();
        await new EfLabControlStore(f.Db).SetEnabledAsync(LabControlKeys.Publisher, false);
        await new EfLabControlStore(f.Db).SetEnabledAsync(LabControlKeys.Failure, true);

        var result = await f.Service().GetAsync();

        Assert.Equal(2, result.Today.OrderCnt);
        Assert.Equal(1, result.Today.PaidCnt);
        Assert.Equal(1, result.Today.CancelCnt);
        Assert.Equal(20m, result.Today.Gmv);

        Assert.Equal(2, result.Inventory.SkuCount);
        Assert.Equal(1, result.Inventory.LowStockCount);
        Assert.Equal(105, result.Inventory.TotalAvailable);
        Assert.Equal(10, result.Inventory.LowStockThreshold);

        Assert.False(result.Queues.Available);
        Assert.Equal(-1, result.Queues.Fulfillment);
        Assert.Equal(-1, result.Queues.Dlq);

        Assert.Equal(2, result.OutboxPending);
        Assert.Equal(1, result.DeadLetterPending);
        Assert.Equal(1, result.AlertsActive);   // 仅非静默告警为活跃

        Assert.True(result.Controls.ConsumerEnabled);
        Assert.False(result.Controls.PublisherEnabled);
        Assert.True(result.Controls.FailureInjectionEnabled);

        Assert.Equal("sqlite", result.Source.Database);
        Assert.Equal("not_configured", result.Source.Redis);
        Assert.Equal("not_configured", result.Source.Rabbitmq);
    }

    [Fact]
    public async Task Available_queue_depth_is_reflected_and_source_marks_connected()
    {
        using var f = new Fixture();
        var snapshot = new QueueDepthSnapshot(3, 4, 5, 6, Available: true);
        var env = new DashboardEnvironment("mysql", true, true);

        var result = await f.Service(snapshot, env).GetAsync();

        Assert.True(result.Queues.Available);
        Assert.Equal(3, result.Queues.Fulfillment);
        Assert.Equal(4, result.Queues.OrderTimeout);
        Assert.Equal(5, result.Queues.FulfillmentRetry);
        Assert.Equal(6, result.Queues.Dlq);
        Assert.Equal("mysql", result.Source.Database);
        Assert.Equal("configured", result.Source.Redis);
        Assert.Equal("connected", result.Source.Rabbitmq);
    }

    [Fact]
    public async Task Configured_but_unavailable_rabbit_is_marked_unavailable()
    {
        using var f = new Fixture();
        var env = new DashboardEnvironment("mysql", false, true);

        var result = await f.Service(QueueDepthSnapshot.Unavailable(), env).GetAsync();

        Assert.False(result.Queues.Available);
        Assert.Equal("unavailable", result.Source.Rabbitmq);
    }
}
