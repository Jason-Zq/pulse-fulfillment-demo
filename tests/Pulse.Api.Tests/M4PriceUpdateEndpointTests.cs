using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Api.Tests;

/// <summary>
/// M4 契约对齐 #4：POST /api/products/prices 批量改价。
/// 请求 { sku, price }[]，校验 price > 0（非法 → 400），未知 SKU 跳过并只计成功数，
/// 事务内更新 Product.Price，返回 { updated }。独立临时 SQLite 库。
/// </summary>
public class M4PriceUpdateEndpointTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string? _previousSqlite;
    private readonly WebApplicationFactory<Program> _factory;

    public M4PriceUpdateEndpointTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pulse-m4-prices-{Guid.NewGuid():N}.db");
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
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // 临时文件清理失败不影响测试结论。
        }
    }

    private decimal GetPrice(string sku)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        return db.Products.Single(p => p.Sku == sku).Price;
    }

    [Fact]
    public async Task Post_prices_updates_prices_and_returns_updated_count()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/products/prices",
            new[]
            {
                new { sku = "SKU-1", price = 88m },
                new { sku = "SKU-2", price = 66m }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, doc.RootElement.GetProperty("updated").GetInt32());
        Assert.Equal(88m, GetPrice("SKU-1"));
        Assert.Equal(66m, GetPrice("SKU-2"));
    }

    [Fact]
    public async Task Post_prices_with_non_positive_price_returns_400_and_no_change()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/products/prices",
            new[] { new { sku = "SKU-1", price = 0m } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(99.5m, GetPrice("SKU-1"));
    }

    [Fact]
    public async Task Post_prices_rejects_whole_batch_when_any_price_invalid()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/products/prices",
            new[]
            {
                new { sku = "SKU-1", price = 77m },
                new { sku = "SKU-2", price = -1m }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // 前置校验整批拒绝，合法项也不产生部分更新。
        Assert.Equal(99.5m, GetPrice("SKU-1"));
        Assert.Equal(199m, GetPrice("SKU-2"));
    }

    [Fact]
    public async Task Post_prices_skips_unknown_sku_and_counts_only_updated()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/products/prices",
            new[]
            {
                new { sku = "SKU-1", price = 77m },
                new { sku = "SKU-UNKNOWN", price = 10m }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, doc.RootElement.GetProperty("updated").GetInt32());
        Assert.Equal(77m, GetPrice("SKU-1"));
    }
}
