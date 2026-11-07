using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Api.Tests;

/// <summary>
/// M4 契约对齐 #3：GET /api/products 每个 item 须返回 8 个字段
/// { id, sku, name, price, available, reserved, deducted, status }。
/// deducted = 同 SKU 下 ChangeType=="Deduct" 的 Quantity 之和（无流水为 0），
/// status = IsActive ? "active" : "inactive"。独立临时 SQLite 库。
/// </summary>
public class M4ProductsFieldsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string? _previousSqlite;
    private readonly WebApplicationFactory<Program> _factory;

    public M4ProductsFieldsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pulse-m4-products-{Guid.NewGuid():N}.db");
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

    /// <summary>造一个带库存与流水的商品，返回其数据库 Id。</summary>
    private long SeedProduct(
        string sku,
        decimal price,
        int available,
        int reserved,
        bool isActive,
        params int[] deductQuantities)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var product = new Product
        {
            Sku = sku,
            Name = $"商品-{sku}",
            Price = price,
            IsActive = isActive
        };
        db.Products.Add(product);
        db.Inventories.Add(new Inventory
        {
            Sku = sku,
            Available = available,
            Reserved = reserved,
            Version = 0
        });
        foreach (var qty in deductQuantities)
        {
            db.InventoryLedgers.Add(new InventoryLedger
            {
                Sku = sku,
                ChangeType = "Deduct",
                Quantity = qty,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        // 非 Deduct 流水不应计入 deducted。
        db.InventoryLedgers.Add(new InventoryLedger
        {
            Sku = sku,
            ChangeType = "Reserve",
            Quantity = 999,
            CreatedAtUtc = DateTime.UtcNow
        });
        // 其它 SKU 的 Deduct 流水不应串入本 SKU 的聚合。
        db.InventoryLedgers.Add(new InventoryLedger
        {
            Sku = "SKU-NOISE",
            ChangeType = "Deduct",
            Quantity = 1000,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
        return product.Id;
    }

    private static JsonElement? FindBySku(JsonElement items, string sku)
    {
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("sku").GetString() == sku)
            {
                return item;
            }
        }
        return null;
    }

    [Fact]
    public async Task Get_products_returns_all_eight_fields_with_correct_values()
    {
        var expectedId = SeedProduct("SKU-CONTRACT", 42.5m, 10, 3, true, 4, 6);
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = FindBySku(doc.RootElement, "SKU-CONTRACT");
        Assert.NotNull(item);
        var el = item!.Value;

        Assert.True(el.TryGetProperty("id", out var idEl), "GET /api/products item 缺少 id 字段");
        Assert.Equal(expectedId, idEl.GetInt64());
        Assert.Equal("SKU-CONTRACT", el.GetProperty("sku").GetString());
        Assert.True(el.TryGetProperty("name", out _), "缺少 name 字段");
        Assert.Equal(42.5m, el.GetProperty("price").GetDecimal());
        Assert.Equal(10, el.GetProperty("available").GetInt32());
        Assert.True(el.TryGetProperty("reserved", out var reservedEl), "GET /api/products item 缺少 reserved 字段");
        Assert.Equal(3, reservedEl.GetInt32());
        Assert.True(el.TryGetProperty("deducted", out var deductedEl), "GET /api/products item 缺少 deducted 字段");
        Assert.Equal(10, deductedEl.GetInt32()); // 4 + 6；Reserve 999 与其它 SKU 的 1000 不计入
        Assert.True(el.TryGetProperty("status", out var statusEl), "GET /api/products item 缺少 status 字段");
        Assert.Equal("active", statusEl.GetString());
    }

    [Fact]
    public async Task Get_products_deducted_is_zero_without_deduct_ledger()
    {
        // 使用 Program 启动播种的 SKU-1（99.5 / Available 100 / Reserved 0 / 无任何流水）。
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = FindBySku(doc.RootElement, "SKU-1");
        Assert.NotNull(item);
        var el = item!.Value;
        Assert.True(el.TryGetProperty("id", out var idEl), "GET /api/products item 缺少 id 字段");
        Assert.True(idEl.GetInt64() > 0);
        Assert.True(el.TryGetProperty("reserved", out var reservedEl), "GET /api/products item 缺少 reserved 字段");
        Assert.Equal(0, reservedEl.GetInt32());
        Assert.True(el.TryGetProperty("deducted", out var deductedEl), "GET /api/products item 缺少 deducted 字段");
        Assert.Equal(0, deductedEl.GetInt32());
        Assert.True(el.TryGetProperty("status", out var statusEl), "GET /api/products item 缺少 status 字段");
        Assert.Equal("active", statusEl.GetString());
    }

    [Fact]
    public async Task Get_products_status_is_inactive_for_inactive_product()
    {
        SeedProduct("SKU-INACTIVE", 10m, 5, 0, false);
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = FindBySku(doc.RootElement, "SKU-INACTIVE");
        Assert.NotNull(item);
        var el = item!.Value;
        Assert.True(el.TryGetProperty("status", out var statusEl), "GET /api/products item 缺少 status 字段");
        Assert.Equal("inactive", statusEl.GetString());
        Assert.True(el.TryGetProperty("deducted", out var deductedEl), "GET /api/products item 缺少 deducted 字段");
        Assert.Equal(0, deductedEl.GetInt32());
    }
}
