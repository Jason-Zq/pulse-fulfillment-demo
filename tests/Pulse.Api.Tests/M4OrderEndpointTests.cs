using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Domain;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Api.Tests;

/// <summary>
/// M4 4.4：API 端点集成测试（WebApplicationFactory）。
/// 独立临时 SQLite 文件库（避免污染共享 pulse-demo.db），Dispose 恢复环境变量并清理。
/// 响应字段严格按 PLAN「DTO 契约」表断言。
/// </summary>
public class M4OrderEndpointTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string? _previousSqlite;
    private readonly WebApplicationFactory<Program> _factory;

    public M4OrderEndpointTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pulse-m4-{Guid.NewGuid():N}.db");
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

    private long SeedOrder(
        OrderStatus status = OrderStatus.Created,
        string sku = "SKU-1",
        int quantity = 2,
        decimal totalAmount = 20m,
        DateTime? createdAtUtc = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var created = createdAtUtc ?? DateTime.UtcNow;
        var order = new Order
        {
            OrderNo = $"PO-{Guid.NewGuid():N}",
            IdempotencyKey = $"m4-{Guid.NewGuid():N}",
            TotalAmount = totalAmount,
            Status = status,
            CreatedAtUtc = created,
            PaidAtUtc = status == OrderStatus.Created ? null : created.AddMinutes(1),
            ExpiresAtUtc = created.AddMinutes(15),
            Version = 0,
            Items =
            {
                new OrderItem { Sku = sku, Quantity = quantity, UnitPrice = 10m }
            }
        };
        db.Orders.Add(order);
        db.SaveChanges();
        return order.Id;
    }

    // ---- POST /api/orders/{id}/cancel ----

    [Fact]
    public async Task Post_cancel_returns_200_and_status_cancelled()
    {
        var client = _factory.CreateClient();
        var id = SeedOrder(OrderStatus.Created);

        var response = await client.PostAsync($"/api/orders/{id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.Equal("Cancelled", body?.Status);
    }

    [Fact]
    public async Task Post_cancel_missing_order_returns_404_with_error()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/orders/999999/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("error", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_cancel_illegal_transition_returns_404_with_error()
    {
        var client = _factory.CreateClient();
        var id = SeedOrder(OrderStatus.Shipped);

        var response = await client.PostAsync($"/api/orders/{id}/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("error", await response.Content.ReadAsStringAsync());
    }

    // ---- POST /api/orders/{id}/ship ----

    [Fact]
    public async Task Post_ship_returns_200_and_status_shipped()
    {
        var client = _factory.CreateClient();
        var id = SeedOrder(OrderStatus.Paid);

        var response = await client.PostAsync($"/api/orders/{id}/ship", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.Equal("Shipped", body?.Status);
    }

    [Fact]
    public async Task Post_ship_missing_order_returns_404_with_error()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/orders/999999/ship", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("error", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_ship_illegal_transition_returns_404_with_error()
    {
        var client = _factory.CreateClient();
        var id = SeedOrder(OrderStatus.Created);

        var response = await client.PostAsync($"/api/orders/{id}/ship", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("error", await response.Content.ReadAsStringAsync());
    }

    // ---- GET /api/orders ----

    [Fact]
    public async Task Get_orders_returns_paged_contract_in_created_at_desc_order()
    {
        var client = _factory.CreateClient();
        SeedOrder(OrderStatus.Created, "SKU-1", createdAtUtc: new DateTime(2026, 11, 7, 1, 0, 0, DateTimeKind.Utc));
        SeedOrder(OrderStatus.Paid, "SKU-2", createdAtUtc: new DateTime(2026, 11, 7, 2, 0, 0, DateTimeKind.Utc));

        var response = await client.GetAsync("/api/orders?page=1&pageSize=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(1, root.GetProperty("pageSize").GetInt32());
        var items = root.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        var first = items[0];
        Assert.True(first.TryGetProperty("id", out _));
        Assert.True(first.TryGetProperty("orderNo", out _));
        Assert.Equal("Paid", first.GetProperty("status").GetString());
        Assert.True(first.TryGetProperty("totalAmount", out _));
        Assert.True(first.TryGetProperty("createdAtUtc", out _));
        Assert.True(first.TryGetProperty("expiresAtUtc", out _));
        Assert.Equal("SKU-2", first.GetProperty("sku").GetString());
    }

    [Fact]
    public async Task Get_orders_supports_status_and_sku_filters()
    {
        var client = _factory.CreateClient();
        SeedOrder(OrderStatus.Created, "SKU-1", createdAtUtc: new DateTime(2026, 11, 7, 1, 0, 0, DateTimeKind.Utc));
        SeedOrder(OrderStatus.Paid, "SKU-2", createdAtUtc: new DateTime(2026, 11, 7, 2, 0, 0, DateTimeKind.Utc));

        var response = await client.GetAsync("/api/orders?status=Paid&sku=SKU-2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("total").GetInt32());
        Assert.Equal("Paid", root.GetProperty("items")[0].GetProperty("status").GetString());
    }

    // ---- GET /api/orders/{id} ----

    [Fact]
    public async Task Get_order_detail_includes_paid_at_utc_and_timeline()
    {
        var client = _factory.CreateClient();
        var id = SeedOrder(OrderStatus.Paid, "SKU-1");

        var response = await client.GetAsync($"/api/orders/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("id", out _));
        Assert.True(root.TryGetProperty("orderNo", out _));
        Assert.Equal("Paid", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("totalAmount", out _));
        Assert.True(root.TryGetProperty("createdAtUtc", out _));
        Assert.True(root.TryGetProperty("paidAtUtc", out _));   // 现有端点缺，RED 失败点
        Assert.True(root.TryGetProperty("expiresAtUtc", out _));
        var items = root.GetProperty("items");
        Assert.True(items[0].TryGetProperty("sku", out _));
        Assert.True(items[0].TryGetProperty("quantity", out _));
        Assert.True(items[0].TryGetProperty("unitPrice", out _));

        var timeline = root.GetProperty("timeline");           // 现有端点缺，RED 失败点
        Assert.True(timeline.GetArrayLength() >= 2);
        foreach (var node in timeline.EnumerateArray())
        {
            Assert.True(node.TryGetProperty("status", out _));
            Assert.True(node.TryGetProperty("atUtc", out _));
            Assert.True(node.TryGetProperty("note", out _));
        }
        var statuses = timeline.EnumerateArray()
            .Select(x => x.GetProperty("status").GetString()).ToList();
        Assert.Contains("Created", statuses);
        Assert.Contains("Paid", statuses);
    }

    // ---- GET /api/reports/daily?date= ----

    [Fact]
    public async Task Get_daily_report_returns_contract()
    {
        var client = _factory.CreateClient();
        var day = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        SeedOrder(OrderStatus.Paid, "SKU-1", 2, 20m, day);

        var response = await client.GetAsync("/api/reports/daily?date=2026-11-07");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("2026-11-07", root.GetProperty("statDate").GetString());
        Assert.Equal(1, root.GetProperty("orderCnt").GetInt32());
        Assert.Equal(1, root.GetProperty("paidCnt").GetInt32());
        Assert.True(root.TryGetProperty("cancelCnt", out _));
        Assert.True(root.TryGetProperty("gmv", out _));
        var recon = root.GetProperty("reconciliation");
        Assert.True(recon.TryGetProperty("orderAmount", out _));
        Assert.True(recon.TryGetProperty("itemAmount", out _));
        Assert.True(recon.TryGetProperty("ledgerReserveQty", out _));
        Assert.True(recon.TryGetProperty("diff", out _));
    }

    private sealed record StatusResponse(string Status);
}
