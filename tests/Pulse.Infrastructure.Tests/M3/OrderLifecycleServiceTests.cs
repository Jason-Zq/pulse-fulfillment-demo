using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Domain;
using Pulse.Infrastructure.Idempotency;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Orders;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M3;

public class OrderLifecycleServiceTests
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

        public long Seed(OrderStatus status)
        {
            var now = DateTime.UtcNow;
            var order = new Order
            {
                OrderNo = "PO-1",
                IdempotencyKey = "idem-1",
                TotalAmount = 20m,
                Status = status,
                CreatedAtUtc = now,
                PaidAtUtc = status == OrderStatus.Created ? null : now,
                ExpiresAtUtc = now.AddMinutes(15),
                Version = 0,
                Items =
                {
                    new OrderItem { Sku = "SKU-1", Quantity = 2, UnitPrice = 10m }
                }
            };
            Db.Orders.Add(order);
            Db.SaveChanges();
            var id = order.Id;
            Db.ChangeTracker.Clear();
            return id;
        }

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    [Fact]
    public async Task Pay_created_order_transitions_to_paid_and_writes_one_outbox()
    {
        using var f = new Fixture();
        var id = f.Seed(OrderStatus.Created);

        var status = await new OrderLifecycleService(f.Db).PayAsync(id);

        Assert.Equal(OrderStatus.Paid, status);
        var order = await f.Db.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.NotNull(order.PaidAtUtc);

        var message = await f.Db.OutboxMessages.SingleAsync();
        Assert.Equal(OrderTopology.RoutingKeyOrderPaid, message.RoutingKey);
        Assert.False(string.IsNullOrWhiteSpace(message.EventId));
        Assert.Equal("pending", message.Status);
        Assert.Contains("orderNo", message.Payload);
        Assert.DoesNotContain("eventId", message.Payload);
    }

    [Fact]
    public async Task Pay_is_idempotent_for_already_paid_order()
    {
        using var f = new Fixture();
        var id = f.Seed(OrderStatus.Paid);

        var status = await new OrderLifecycleService(f.Db).PayAsync(id);

        Assert.Equal(OrderStatus.Paid, status);
        Assert.Equal(0, await f.Db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Pay_missing_order_throws()
    {
        using var f = new Fixture();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OrderLifecycleService(f.Db).PayAsync(999));

        Assert.Equal(0, await f.Db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Pay_cancelled_order_throws_illegal_transition()
    {
        using var f = new Fixture();
        var id = f.Seed(OrderStatus.Cancelled);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OrderLifecycleService(f.Db).PayAsync(id));

        Assert.Contains("非法", ex.Message);
        Assert.Equal(0, await f.Db.OutboxMessages.CountAsync());
        Assert.Equal(OrderStatus.Cancelled, (await f.Db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public void M3_services_resolve_from_di_scope()
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

        Assert.IsType<EventDeduplicator>(
            scope.ServiceProvider.GetRequiredService<IEventDeduplicator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<FulfillmentService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OrderLifecycleService>());
    }
}
