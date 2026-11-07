using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Pulse.Api.Tests.M6c;

/// <summary>
/// M6c 6.10 RED：GET /api/pipeline 此刻不存在 → 404。
/// 独立临时 SQLite 文件库；Program.cs 顶层读连接串，必须走环境变量。
/// </summary>
public class PipelineEndpointTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string? _previousSqlite;
    private readonly WebApplicationFactory<Program> _factory;

    public PipelineEndpointTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pulse-m6c-{Guid.NewGuid():N}.db");
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

    [Fact]
    public async Task Pipeline_returns_seven_nodes_with_exact_fieldset()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/pipeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("generatedAtUtc", out _));
        Assert.True(root.TryGetProperty("nodes", out var nodes));
        Assert.Equal(7, nodes.GetArrayLength());

        var expected = new[]
        {
            "key", "label", "middleware", "count", "unit", "source", "available", "link"
        };
        foreach (var node in nodes.EnumerateArray())
        {
            Assert.Equal(expected.OrderBy(x => x),
                node.EnumerateObject().Select(p => p.Name).OrderBy(x => x));
            Assert.True(node.GetProperty("count").GetInt64() >= -1);
        }
    }

    [Fact]
    public async Task Swagger_document_is_served_when_enabled_by_default()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("paths", out var paths));
        Assert.True(paths.TryGetProperty("/api/pipeline", out _));
    }
}
