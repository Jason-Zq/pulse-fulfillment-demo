using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Infrastructure.Experiments;
using Pulse.Infrastructure.Orders;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M5;

/// <summary>
/// M5 5.6：流量生成器吞吐 / P99（P99 用排序后索引）、独立 scope + 唯一幂等键、
/// 以及 GenerateAsync 落 TRAFFIC 实验行（params/result 为 JSON 字符串）。
/// </summary>
public class TrafficGeneratorTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _seedScope;

        public PulseDbContext Db { get; }
        public TrafficGenerator Generator { get; }

        public Fixture()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var services = new ServiceCollection();
            services.AddSingleton(_connection);
            services.AddDbContext<PulseDbContext>((sp, o) =>
                o.UseSqlite(sp.GetRequiredService<SqliteConnection>()));
            services.AddScoped<PlaceOrderService>();
            _provider = services.BuildServiceProvider();
            _seedScope = _provider.CreateScope();
            Db = _seedScope.ServiceProvider.GetRequiredService<PulseDbContext>();
            Db.Database.EnsureCreated();
            Generator = new TrafficGenerator(_provider.GetRequiredService<IServiceScopeFactory>());

            Db.Products.Add(new Product { Sku = "SKU-1", Name = "演示商品 A", Price = 10m, IsActive = true });
            Db.Inventories.Add(new Inventory { Sku = "SKU-1", Available = 100, Reserved = 0, Version = 0 });
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _seedScope.Dispose();
            _provider.Dispose();
            Db.Dispose();
            _connection.Dispose();
        }
    }

    [Fact]
    public void P99_uses_sorted_index_after_ordering()
    {
        var samples = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        Assert.Equal(99d, TrafficGenerator.P99(samples));   // ceil(0.99*100)-1 = 98 -> 第 99 个
        Assert.Equal(0d, TrafficGenerator.P99(Array.Empty<double>()));
        Assert.Equal(5d, TrafficGenerator.P99(new[] { 5d }));
        Assert.Equal(9d, TrafficGenerator.P99(new[] { 9d, 1d, 3d, 7d })); // 有序 1,3,7,9 -> ceil(3.96)-1=3 -> 9
    }

    [Fact]
    public void ThroughputRps_is_created_over_elapsed_seconds()
    {
        Assert.Equal(200d, TrafficGenerator.ThroughputRps(100, 500));
        Assert.Equal(0d, TrafficGenerator.ThroughputRps(0, 0));
        Assert.Equal(0d, TrafficGenerator.ThroughputRps(10, -1));
    }

    [Fact]
    public async Task GenerateCore_creates_one_order_per_request_with_unique_idempotency_key()
    {
        using var f = new Fixture();

        var metrics = await f.Generator.GenerateCoreAsync(
            new TrafficRequest("SKU-1", 5, 0, 1, 1));

        Assert.Equal(5, metrics.Requested);
        Assert.Equal(5, metrics.Created);
        Assert.Equal(0, metrics.Failed);
        Assert.True(metrics.ElapsedMs >= 0);
        Assert.True(metrics.ThroughputRps > 0);
        Assert.True(metrics.P99Ms >= 0);
        Assert.Equal(5, await f.Db.Orders.AsNoTracking().CountAsync());
        Assert.Equal(5, await f.Db.OutboxMessages.AsNoTracking().CountAsync());
        // 每单独立幂等键：5 个不同值。
        Assert.Equal(5, await f.Db.Orders.AsNoTracking().Select(o => o.IdempotencyKey).Distinct().CountAsync());
    }

    [Fact]
    public async Task GenerateAsync_persists_TRAFFIC_experiment_row_with_json_strings()
    {
        using var f = new Fixture();

        var result = await f.Generator.GenerateAsync(
            new TrafficRequest("SKU-1", 3, 0, 1, 1));

        Assert.Equal(3, result.Created);
        Assert.True(result.ExperimentId > 0);
        var row = await f.Db.ExperimentResultRecords.AsNoTracking().SingleAsync();
        Assert.Equal("TRAFFIC", row.Name);
        Assert.True(row.Passed);
        using var p = JsonDocument.Parse(row.Params);
        using var r = JsonDocument.Parse(row.Result);
        Assert.Equal("SKU-1", p.RootElement.GetProperty("sku").GetString());
        Assert.True(r.RootElement.TryGetProperty("throughputRps", out _));
        Assert.True(r.RootElement.TryGetProperty("p99Ms", out _));
    }
}
