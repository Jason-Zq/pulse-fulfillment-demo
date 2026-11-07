using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Api.Tests;

/// <summary>
/// M4 契约对齐 #2：POST /api/orders 响应须含 id，且等于库中该订单 Id；
/// 幂等重放（命中 IdempotencyKey）也必须返回同一 id。
/// 独立临时 SQLite 文件库，避免污染共享 pulse-demo.db。
/// </summary>
public class M4PlaceOrderIdTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string? _previousSqlite;
    private readonly WebApplicationFactory<Program> _factory;

    public M4PlaceOrderIdTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pulse-m4-id-{Guid.NewGuid():N}.db");
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

    private long FindPersistedOrderId(string orderNo)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        return db.Orders.AsNoTracking().Single(o => o.OrderNo == orderNo).Id;
    }

    [Fact]
    public async Task Post_order_response_contains_id_equal_to_persisted_order()
    {
        var client = _factory.CreateClient();
        var key = $"m4-id-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/api/orders",
            new { sku = "SKU-1", quantity = 2, idempotencyKey = key });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("id", out var idEl), "POST /api/orders 响应缺少 id 字段");
        var responseId = idEl.GetInt64();
        Assert.True(responseId > 0, "id 应为数据库生成的正整数");

        var orderNo = root.GetProperty("orderNo").GetString();
        Assert.False(string.IsNullOrWhiteSpace(orderNo));
        Assert.Equal(FindPersistedOrderId(orderNo!), responseId);
    }

    [Fact]
    public async Task Idempotent_replay_returns_same_id_equal_to_persisted_order()
    {
        var client = _factory.CreateClient();
        var key = $"m4-idem-id-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/api/orders",
            new { sku = "SKU-1", quantity = 1, idempotencyKey = key });
        var second = await client.PostAsJsonAsync("/api/orders",
            new { sku = "SKU-1", quantity = 1, idempotencyKey = key });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var firstRoot = firstDoc.RootElement;
        var secondRoot = secondDoc.RootElement;
        Assert.True(firstRoot.TryGetProperty("id", out var firstIdEl), "首次响应缺少 id 字段");
        Assert.True(secondRoot.TryGetProperty("id", out var secondIdEl), "幂等重放响应缺少 id 字段");

        var firstId = firstIdEl.GetInt64();
        var secondId = secondIdEl.GetInt64();
        Assert.True(firstId > 0);
        Assert.Equal(firstId, secondId);
        Assert.Equal(firstRoot.GetProperty("orderNo").GetString(), secondRoot.GetProperty("orderNo").GetString());
        Assert.Equal(FindPersistedOrderId(firstRoot.GetProperty("orderNo").GetString()!), firstId);
    }
}
