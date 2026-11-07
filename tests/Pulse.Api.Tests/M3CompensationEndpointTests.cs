using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Domain;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Api.Tests;

public class M3CompensationEndpointTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string? _previousSqlite;
    private readonly WebApplicationFactory<Program> _factory;

    public M3CompensationEndpointTests()
    {
        // 独立临时 SQLite 库：EnsureCreated() 会按当前模型建出 M3 新表。
        // 共享的 pulse-demo.db 只有 M1 的 6 张旧表，EnsureCreated 不会补表。
        _dbPath = Path.Combine(Path.GetTempPath(), $"pulse-m3-{Guid.NewGuid():N}.db");
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

    private long SeedOrder()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var now = DateTime.UtcNow;
        var order = new Order
        {
            OrderNo = $"PO-{Guid.NewGuid():N}",
            IdempotencyKey = $"m3-{Guid.NewGuid():N}",
            TotalAmount = 20m,
            Status = OrderStatus.Created,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(15),
            Version = 0
        };
        db.Orders.Add(order);
        db.SaveChanges();
        return order.Id;
    }

    private long SeedDeadLetter(string status = "pending")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var letter = new DeadLetter
        {
            OriginalEventId = $"evt-{Guid.NewGuid():N}",
            Payload = "{\"orderNo\":\"PO-1\"}",
            Error = "boom",
            RetryCount = 3,
            Status = status,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.DeadLetters.Add(letter);
        db.SaveChanges();
        return letter.Id;
    }

    [Fact]
    public async Task Post_pay_returns_200_and_status_paid()
    {
        var client = _factory.CreateClient();
        var id = SeedOrder();

        var response = await client.PostAsync($"/api/orders/{id}/pay", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.Equal("Paid", body?.Status);
    }

    [Fact]
    public async Task Post_pay_missing_order_returns_404()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/orders/999999/pay", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("error", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_dead_letters_returns_list_and_supports_status_filter()
    {
        var client = _factory.CreateClient();
        SeedDeadLetter("pending");
        SeedDeadLetter("handled");

        var all = await client.GetAsync("/api/dead-letters");
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        using (var doc = JsonDocument.Parse(await all.Content.ReadAsStringAsync()))
        {
            Assert.Equal(2, doc.RootElement.GetArrayLength());
        }

        var pending = await client.GetAsync("/api/dead-letters?status=pending");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        using (var doc = JsonDocument.Parse(await pending.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, doc.RootElement.GetArrayLength());
            Assert.Equal("pending", doc.RootElement[0].GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task Post_retry_returns_200_and_status_handled()
    {
        var client = _factory.CreateClient();
        var id = SeedDeadLetter();

        var response = await client.PostAsync($"/api/dead-letters/{id}/retry", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.Equal("handled", body?.Status);
    }

    [Fact]
    public async Task Post_retry_missing_id_returns_404()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/dead-letters/999999/retry", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("error", await response.Content.ReadAsStringAsync());
    }

    private sealed record StatusResponse(string Status);
}
