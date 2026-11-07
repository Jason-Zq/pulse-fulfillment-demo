using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Api.Tests;

/// <summary>M5 5.2：GET /api/reports/daily 只追加 persisted 字段，M4 既有字段不变。</summary>
public class M5DailyReportPersistedTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string? _previousSqlite;
    private readonly WebApplicationFactory<Program> _factory;

    public M5DailyReportPersistedTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pulse-m5-report-{Guid.NewGuid():N}.db");
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

    [Fact]
    public async Task Daily_report_has_persisted_false_and_keeps_M4_fields()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/reports/daily?date=2026-11-07");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        foreach (var name in new[] { "statDate", "orderCnt", "paidCnt", "cancelCnt", "gmv", "reconciliation", "persisted" })
        {
            Assert.True(root.TryGetProperty(name, out _), $"缺少字段 {name}");
        }

        Assert.False(root.GetProperty("persisted").GetBoolean());
        Assert.Equal("2026-11-07", root.GetProperty("statDate").GetString());
    }

    [Fact]
    public async Task Daily_report_persisted_true_after_row_is_written()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
            db.DailyReportRecords.Add(new DailyReportRecord
            {
                StatDate = new DateOnly(2026, 11, 7),
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/reports/daily?date=2026-11-07");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("persisted").GetBoolean());
    }
}
