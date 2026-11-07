using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Dashboard;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M6c;

/// <summary>
/// M6c 6.10 RED：链路泳道 7 个节点的计数必须来自真实查询，且队列不可用时
/// 不伪造 0（available=false + count=-1）。此时 PipelineService 尚不存在 → 编译失败。
/// </summary>
public class PipelineServiceTests
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

        public PipelineService Service(QueueDepthSnapshot? snapshot = null) =>
            new(Db, new FakeQueues(snapshot ?? QueueDepthSnapshot.Unavailable()));

        public void Seed()
        {
            var now = DateTime.UtcNow;
            Db.Orders.Add(new Order
            {
                OrderNo = "PIPE-1", IdempotencyKey = "pipe-1", TotalAmount = 10m,
                Status = OrderStatus.Shipped, CreatedAtUtc = now, PaidAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(15), Version = 0
            });
            Db.OutboxMessages.AddRange(
                new OutboxMessage { EventId = "e1", RoutingKey = "order.created", Payload = "{}", Status = "pending", CreatedAtUtc = now },
                new OutboxMessage { EventId = "e2", RoutingKey = "order.paid", Payload = "{}", Status = "published", CreatedAtUtc = now });
            Db.ProcessedEvents.Add(new ProcessedEvent { EventId = "e2", ProcessedAtUtc = now });
            Db.InventoryLedgers.AddRange(
                new InventoryLedger { Sku = "SKU-1", ChangeType = "Reserve", Quantity = 1, CreatedAtUtc = now },
                new InventoryLedger { Sku = "SKU-1", ChangeType = "Deduct", Quantity = 1, CreatedAtUtc = now });
            Db.DeadLetters.Add(new DeadLetter
            {
                OriginalEventId = "dl-1", Payload = "{}", Error = "boom", RetryCount = 3,
                Status = "pending", CreatedAtUtc = now
            });
            Db.DailyReportRecords.Add(new DailyReportRecord
            {
                StatDate = DateOnly.FromDateTime(now), OrderCnt = 1, PaidCnt = 1,
                CancelCnt = 0, Gmv = 10m, CreatedAtUtc = now
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

    [Fact]
    public async Task Returns_seven_nodes_with_real_counts()
    {
        using var f = new Fixture();
        f.Seed();

        var result = await f.Service(new QueueDepthSnapshot(2, 0, 0, 0, Available: true)).GetAsync();

        Assert.Equal(7, result.Nodes.Count);
        var byKey = result.Nodes.ToDictionary(n => n.Key);

        Assert.Equal(1, byKey["orders"].Count);          // Orders 表真实行数
        Assert.Equal(1, byKey["outbox"].Count);         // 仅 status=pending
        Assert.Equal(2, byKey["exchange"].Count);       // q.fulfillment 真实队列深度
        Assert.Equal(1, byKey["worker"].Count);         // processed_events 真实行数
        Assert.Equal(1, byKey["deduct"].Count);         // Deduct 流水真实行数
        Assert.Equal(1, byKey["deadLetter"].Count);     // 仅 status=pending
        Assert.Equal(1, byKey["report"].Count);         // daily_reports 真实行数

        Assert.All(result.Nodes, n => Assert.True(n.Available));
        Assert.All(result.Nodes, n => Assert.False(string.IsNullOrWhiteSpace(n.Source)));
        Assert.All(result.Nodes, n => Assert.False(string.IsNullOrWhiteSpace(n.Link)));
    }

    [Fact]
    public async Task Unavailable_queue_node_is_minus_one_and_not_faked_as_zero()
    {
        using var f = new Fixture();

        var result = await f.Service(QueueDepthSnapshot.Unavailable()).GetAsync();
        var exchange = result.Nodes.Single(n => n.Key == "exchange");

        Assert.False(exchange.Available);
        Assert.Equal(-1, exchange.Count);
        // 其余节点仍给出真实计数（空库为 0），不受队列不可用影响。
        Assert.All(result.Nodes.Where(n => n.Key != "exchange"), n => Assert.True(n.Available));
        Assert.Equal(0, result.Nodes.Single(n => n.Key == "orders").Count);
    }
}
