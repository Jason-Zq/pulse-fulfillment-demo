using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Api.Tests.M6;

/// <summary>
/// M6b RED：POST /api/orders 支持 items[] 多明细（与单 SKU 互斥）、
/// GET /api/products 暴露 version、GET /api/orders 支持 orderNo 模糊筛选。
/// 独立临时 SQLite 文件库；Program.cs 在顶层读连接串，故必须走环境变量
/// （Api.Tests 已禁用并行，环境变量不会串扰其它测试类）。
/// </summary>
public class M6EndpointsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string? _previousSqlite;
    private readonly WebApplicationFactory<Program> _factory;

    public M6EndpointsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pulse-m6-{Guid.NewGuid():N}.db");
        _previousSqlite = Environment.GetEnvironmentVariable("ConnectionStrings__Sqlite");
        Environment.SetEnvironmentVariable("ConnectionStrings__Sqlite", $"Data Source={_dbPath}");
        _factory = new WebApplicationFactory<Program>();
    }

    public void Dispose()
    {
        _factory.Dispose();
        Environment.SetEnvironmentVariable("ConnectionStrings__Sqlite", _previousSqlite);
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // 临时文件清理失败不影响测试结论。
        }
    }

    private PulseDbContext OpenDb() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<PulseDbContext>();

    [Fact]
    public async Task Post_order_with_items_returns_ok_and_writes_all_details()
    {
        var client = _factory.CreateClient();
        var key = $"m6-multi-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[]
            {
                new { sku = "SKU-1", quantity = 2 },
                new { sku = "SKU-2", quantity = 3 }
            },
            idempotencyKey = key
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("id", out var idEl));
        Assert.True(idEl.GetInt64() > 0);

        using var db = OpenDb();
        var order = await db.Orders.Include(o => o.Items)
            .SingleAsync(o => o.IdempotencyKey == key);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(99.5m * 2 + 199m * 3, order.TotalAmount);
        Assert.Equal(2, await db.InventoryLedgers.CountAsync(l => l.ChangeType == "Reserve"));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.RoutingKey == "order.created"));
        Assert.Equal(98, (await db.Inventories.SingleAsync(i => i.Sku == "SKU-1")).Available);
        Assert.Equal(47, (await db.Inventories.SingleAsync(i => i.Sku == "SKU-2")).Available);
    }

    [Fact]
    public async Task Items_and_single_sku_together_return_400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            sku = "SKU-1",
            quantity = 1,
            items = new[] { new { sku = "SKU-2", quantity = 1 } },
            idempotencyKey = $"m6-conflict-{Guid.NewGuid():N}"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("SKU-1", "SKU-2", 0)]
    [InlineData("NO-SUCH-SKU", "SKU-2", 1)]
    public async Task Invalid_items_return_400_without_side_effects(
        string firstSku, string secondSku, int quantity)
    {
        var client = _factory.CreateClient();
        var key = $"m6-invalid-{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[]
            {
                new { sku = firstSku, quantity },
                new { sku = secondSku, quantity = 1 }
            },
            idempotencyKey = key
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var db = OpenDb();
        Assert.Equal(0, await db.Orders.CountAsync(o => o.IdempotencyKey == key));
        Assert.Equal(0, await db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Insufficient_stock_on_second_line_rolls_back_all_lines()
    {
        var client = _factory.CreateClient();
        var key = $"m6-rollback-{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[]
            {
                new { sku = "SKU-1", quantity = 1 },
                new { sku = "SKU-2", quantity = 999999 }
            },
            idempotencyKey = key
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var db = OpenDb();
        Assert.Equal(0, await db.Orders.CountAsync(o => o.IdempotencyKey == key));
        Assert.Equal(0, await db.InventoryLedgers.CountAsync());
        Assert.Equal(100, (await db.Inventories.SingleAsync(i => i.Sku == "SKU-1")).Available);
    }

    [Fact]
    public async Task List_products_exposes_inventory_version()
    {
        var client = _factory.CreateClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/products"));
        Assert.True(doc.RootElement.GetArrayLength() > 0);
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            Assert.True(row.TryGetProperty("version", out var version), "商品响应缺少 version");
            Assert.True(version.GetInt32() >= 0);
        }
    }

    [Fact]
    public async Task List_orders_filters_by_order_no_prefix()
    {
        var client = _factory.CreateClient();
        var prefix = $"m6-prefix-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/orders",
            new { sku = "SKU-1", quantity = 1, idempotencyKey = prefix + "-a" });
        await client.PostAsJsonAsync("/api/orders",
            new { sku = "SKU-1", quantity = 1, idempotencyKey = prefix + "-b" });

        var first = await client.PostAsJsonAsync("/api/orders",
            new { sku = "SKU-1", quantity = 1, idempotencyKey = prefix + "-target" });
        using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var orderNo = firstDoc.RootElement.GetProperty("orderNo").GetString();

        using var listDoc = JsonDocument.Parse(
            await client.GetStringAsync($"/api/orders?orderNo={Uri.EscapeDataString(orderNo!)}"));
        Assert.Equal(1, listDoc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(orderNo,
            listDoc.RootElement.GetProperty("items")[0].GetProperty("orderNo").GetString());
    }
}
