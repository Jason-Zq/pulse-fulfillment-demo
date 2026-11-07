using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Domain;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Orders;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M3;

public class TimeoutOrderServiceTests
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

        public long Seed(
            OrderStatus status,
            DateTime expiresAtUtc,
            int available = 10,
            int reserved = 2,
            int quantity = 2)
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
                ExpiresAtUtc = expiresAtUtc,
                Version = 0
            };
            Db.Products.Add(new Product { Sku = "SKU-1", Name = "演示商品", Price = 10m });
            Db.Inventories.Add(new Inventory
            {
                Sku = "SKU-1",
                Available = available,
                Reserved = reserved,
                Version = 0
            });
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

    private static OrderEvent TimeoutEvent() =>
        new() { OrderNo = "PO-1", Sku = "SKU-1", Quantity = 2 };

    [Fact]
    public async Task Expired_created_order_is_cancelled_and_reserved_released()
    {
        using var f = new Fixture();
        var id = f.Seed(OrderStatus.Created, DateTime.UtcNow.AddMinutes(-1));

        var closed = await new TimeoutOrderService(f.Db).CloseExpiredAsync(TimeoutEvent());

        Assert.True(closed);
        Assert.Equal(OrderStatus.Cancelled, (await f.Db.Orders.SingleAsync()).Status);
        var inventory = await f.Db.Inventories.SingleAsync();
        Assert.Equal(12, inventory.Available);
        Assert.Equal(0, inventory.Reserved);
        Assert.Equal(1, inventory.Version);
        var ledger = await f.Db.InventoryLedgers.SingleAsync();
        Assert.Equal("Release", ledger.ChangeType);
        Assert.Equal(2, ledger.Quantity);
        Assert.Equal(id, ledger.RefOrderId);
    }

    [Fact]
    public async Task Not_yet_expired_order_is_left_untouched()
    {
        using var f = new Fixture();
        f.Seed(OrderStatus.Created, DateTime.UtcNow.AddMinutes(15));

        var closed = await new TimeoutOrderService(f.Db).CloseExpiredAsync(TimeoutEvent());

        Assert.False(closed);
        Assert.Equal(OrderStatus.Created, (await f.Db.Orders.SingleAsync()).Status);
        var inventory = await f.Db.Inventories.SingleAsync();
        Assert.Equal(10, inventory.Available);
        Assert.Equal(2, inventory.Reserved);
        Assert.Equal(0, inventory.Version);
        Assert.Equal(0, await f.Db.InventoryLedgers.CountAsync());
    }

    [Fact]
    public async Task Already_paid_order_is_skipped()
    {
        using var f = new Fixture();
        f.Seed(OrderStatus.Paid, DateTime.UtcNow.AddMinutes(-1));

        var closed = await new TimeoutOrderService(f.Db).CloseExpiredAsync(TimeoutEvent());

        Assert.False(closed);
        Assert.Equal(OrderStatus.Paid, (await f.Db.Orders.SingleAsync()).Status);
        Assert.Equal(2, (await f.Db.Inventories.SingleAsync()).Reserved);
        Assert.Equal(0, await f.Db.InventoryLedgers.CountAsync());
    }

    [Fact]
    public async Task Already_cancelled_order_is_skipped()
    {
        using var f = new Fixture();
        f.Seed(OrderStatus.Cancelled, DateTime.UtcNow.AddMinutes(-1));

        var closed = await new TimeoutOrderService(f.Db).CloseExpiredAsync(TimeoutEvent());

        Assert.False(closed);
        Assert.Equal(0, await f.Db.InventoryLedgers.CountAsync());
    }

    [Fact]
    public async Task Repeat_timeout_event_does_not_release_twice()
    {
        using var f = new Fixture();
        f.Seed(OrderStatus.Created, DateTime.UtcNow.AddMinutes(-1));
        var sut = new TimeoutOrderService(f.Db);

        await sut.CloseExpiredAsync(TimeoutEvent());
        await sut.CloseExpiredAsync(TimeoutEvent());

        Assert.Equal(1, await f.Db.InventoryLedgers.CountAsync());
        var inventory = await f.Db.Inventories.SingleAsync();
        Assert.Equal(12, inventory.Available);
        Assert.Equal(0, inventory.Reserved);
    }

    [Fact]
    public void Timeout_service_resolves_from_di_scope()
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

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TimeoutOrderService>());
    }
}
