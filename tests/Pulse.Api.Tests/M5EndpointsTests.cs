using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Api.Tests;

/// <summary>
/// M5 5.7：9 个 M5 端点的契约集成测试（WebApplicationFactory + 独立临时 SQLite 库）。
/// 实验样本量 / 超时用 Experiments__* 环境变量压到小值，避免请求挂死。
/// 严格 RED：端点此刻不存在 → 404 / 断言失败。
/// </summary>
public class M5EndpointsTests : IDisposable
{
    private static readonly Dictionary<string, string> ExperimentOverrides = new()
    {
        ["Experiments__E1OrderCount"] = "2",
        ["Experiments__E1PollTimeoutSeconds"] = "1",
        ["Experiments__E2DuplicateCount"] = "5",
        ["Experiments__E2ConsumeTimeoutSeconds"] = "1",
        ["Experiments__E3OrderCount"] = "5",
        ["Experiments__E3Concurrency"] = "1",
        ["Experiments__E3Quantity"] = "1",
        ["Experiments__RunTimeoutSeconds"] = "8"
    };

    private readonly string _dbPath;
    private readonly string? _previousSqlite;
    private readonly Dictionary<string, string?> _previousOverrides = new();
    private readonly WebApplicationFactory<Program> _factory;

    public M5EndpointsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pulse-m5-{Guid.NewGuid():N}.db");
        _previousSqlite = Environment.GetEnvironmentVariable("ConnectionStrings__Sqlite");
        Environment.SetEnvironmentVariable("ConnectionStrings__Sqlite", $"Data Source={_dbPath}");
        foreach (var (key, value) in ExperimentOverrides)
        {
            _previousOverrides[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        _factory = new WebApplicationFactory<Program>();
    }

    public void Dispose()
    {
        _factory.Dispose();
        Environment.SetEnvironmentVariable("ConnectionStrings__Sqlite", _previousSqlite);
        foreach (var (key, value) in _previousOverrides)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

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

    private long SeedAlert(string key = "seeded-alert", string level = "warning")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var row = new AlertRecordEntity
        {
            AlertKey = key, Level = level, Message = "测试告警",
            CreatedAtUtc = DateTime.UtcNow, Silenced = false
        };
        db.AlertRecordEntities.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    private long SeedExperiment(string name = "E1")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var row = new ExperimentResultRecord
        {
            Name = name, Params = "{\"k\":1}", Result = "{\"passed\":true}",
            RanAtUtc = DateTime.UtcNow, Passed = true
        };
        db.ExperimentResultRecords.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    private static void AssertHas(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            Assert.True(obj.TryGetProperty(name, out _), $"缺少字段 {name}");
        }
    }

    // ---- 1. GET /api/dashboard ----

    [Fact]
    public async Task Get_dashboard_returns_full_contract_with_unavailable_queues_as_minus_one()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        AssertHas(root, "generatedAtUtc", "today", "inventory", "queues", "outboxPending",
            "deadLetterPending", "alertsActive", "controls", "source");
        AssertHas(root.GetProperty("today"), "orderCnt", "paidCnt", "cancelCnt", "gmv");
        AssertHas(root.GetProperty("inventory"), "skuCount", "lowStockCount", "totalAvailable", "lowStockThreshold");
        var queues = root.GetProperty("queues");
        AssertHas(queues, "fulfillment", "orderTimeout", "fulfillmentRetry", "dlq", "available");
        Assert.False(queues.GetProperty("available").GetBoolean());
        Assert.Equal(-1, queues.GetProperty("fulfillment").GetInt32());  // 未配置不伪造 0
        var controls = root.GetProperty("controls");
        AssertHas(controls, "consumerEnabled", "publisherEnabled", "failureInjectionEnabled");
        // 全新库默认：consumer/publisher 开、故障注入关（否则会毁掉正常履约链路）。
        Assert.True(controls.GetProperty("consumerEnabled").GetBoolean());
        Assert.True(controls.GetProperty("publisherEnabled").GetBoolean());
        Assert.False(controls.GetProperty("failureInjectionEnabled").GetBoolean());
        AssertHas(root.GetProperty("source"), "database", "redis", "rabbitmq");
    }

    // ---- 2. GET /api/alerts ----

    [Fact]
    public async Task Get_alerts_returns_alert_record_array_contract()
    {
        var client = _factory.CreateClient();
        SeedAlert();

        var response = await client.GetAsync("/api/alerts?limit=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        var arr = doc.RootElement;
        Assert.True(arr.GetArrayLength() >= 1);
        var first = arr[0];
        AssertHas(first, "id", "alertKey", "level", "message", "createdAtUtc", "silenced", "silencedUntilUtc");
        Assert.Equal("warning", first.GetProperty("level").GetString());
        Assert.False(first.GetProperty("silenced").GetBoolean());
    }

    // ---- 3. POST /api/alerts/{key}/silence ----

    [Fact]
    public async Task Post_silence_returns_contract_and_validates_minutes_range()
    {
        var client = _factory.CreateClient();
        SeedAlert("to-silence");

        var ok = await client.PostAsJsonAsync("/api/alerts/to-silence/silence", new { minutes = 30 });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (var doc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync()))
        {
            var root = doc.RootElement;
            AssertHas(root, "alertKey", "silenced", "silencedUntilUtc");
            Assert.Equal("to-silence", root.GetProperty("alertKey").GetString());
            Assert.True(root.GetProperty("silenced").GetBoolean());
        }

        var bad = await client.PostAsJsonAsync("/api/alerts/to-silence/silence", new { minutes = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var tooBig = await client.PostAsJsonAsync("/api/alerts/to-silence/silence", new { minutes = 1441 });
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
    }

    // ---- 4. GET /api/lab/state ----

    [Fact]
    public async Task Get_lab_state_returns_contract_with_defaults()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/lab/state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        AssertHas(root, "consumerEnabled", "publisherEnabled", "failureInjectionEnabled",
            "database", "redisConfigured", "rabbitConfigured");
        Assert.True(root.GetProperty("consumerEnabled").GetBoolean());
        Assert.True(root.GetProperty("publisherEnabled").GetBoolean());
        Assert.False(root.GetProperty("failureInjectionEnabled").GetBoolean());
        Assert.False(root.GetProperty("redisConfigured").GetBoolean());
        Assert.False(root.GetProperty("rabbitConfigured").GetBoolean());
    }

    // ---- 5. POST /api/lab/traffic ----

    [Fact]
    public async Task Post_lab_traffic_returns_measured_result_with_experiment_id()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/lab/traffic",
            new { sku = "SKU-1", count = 2, rps = 0, quantity = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        AssertHas(root, "requested", "created", "failed", "elapsedMs", "throughputRps", "p99Ms", "experimentId");
        Assert.Equal(2, root.GetProperty("created").GetInt32());
        Assert.True(root.GetProperty("experimentId").GetInt64() > 0);
        Assert.True(root.GetProperty("elapsedMs").GetDouble() >= 0);
    }

    // ---- 6. POST /api/lab/inject ----

    [Fact]
    public async Task Post_lab_inject_controls_state_and_validates_type()
    {
        var client = _factory.CreateClient();

        var stop = await client.PostAsJsonAsync("/api/lab/inject", new { type = "stop_consumer" });
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        using (var doc = JsonDocument.Parse(await stop.Content.ReadAsStringAsync()))
        {
            AssertHas(doc.RootElement, "type", "applied", "detail");
            Assert.Equal("stop_consumer", doc.RootElement.GetProperty("type").GetString());
            Assert.True(doc.RootElement.GetProperty("applied").GetBoolean());
        }

        var state = await client.GetAsync("/api/lab/state");
        using (var doc = JsonDocument.Parse(await state.Content.ReadAsStringAsync()))
        {
            Assert.False(doc.RootElement.GetProperty("consumerEnabled").GetBoolean());
        }

        var bad = await client.PostAsJsonAsync("/api/lab/inject", new { type = "not_a_type" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Failure_on_off_toggles_state_and_dashboard_consistently()
    {
        var client = _factory.CreateClient();

        // 全新库：false。
        using (var doc = JsonDocument.Parse(await (await client.GetAsync("/api/lab/state")).Content.ReadAsStringAsync()))
        {
            Assert.False(doc.RootElement.GetProperty("failureInjectionEnabled").GetBoolean());
        }

        // failure_on -> true，且 dashboard.controls 同步。
        var on = await client.PostAsJsonAsync("/api/lab/inject", new { type = "failure_on" });
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        using (var doc = JsonDocument.Parse(await on.Content.ReadAsStringAsync()))
        {
            Assert.Equal("failure_on", doc.RootElement.GetProperty("type").GetString());
            Assert.True(doc.RootElement.GetProperty("applied").GetBoolean());
        }
        using (var doc = JsonDocument.Parse(await (await client.GetAsync("/api/lab/state")).Content.ReadAsStringAsync()))
        {
            Assert.True(doc.RootElement.GetProperty("failureInjectionEnabled").GetBoolean());
        }
        using (var doc = JsonDocument.Parse(await (await client.GetAsync("/api/dashboard")).Content.ReadAsStringAsync()))
        {
            Assert.True(doc.RootElement.GetProperty("controls")
                .GetProperty("failureInjectionEnabled").GetBoolean());
        }

        // failure_off -> false，且 dashboard.controls 同步。
        var off = await client.PostAsJsonAsync("/api/lab/inject", new { type = "failure_off" });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        using (var doc = JsonDocument.Parse(await off.Content.ReadAsStringAsync()))
        {
            Assert.Equal("failure_off", doc.RootElement.GetProperty("type").GetString());
        }
        using (var doc = JsonDocument.Parse(await (await client.GetAsync("/api/lab/state")).Content.ReadAsStringAsync()))
        {
            Assert.False(doc.RootElement.GetProperty("failureInjectionEnabled").GetBoolean());
        }
        using (var doc = JsonDocument.Parse(await (await client.GetAsync("/api/dashboard")).Content.ReadAsStringAsync()))
        {
            Assert.False(doc.RootElement.GetProperty("controls")
                .GetProperty("failureInjectionEnabled").GetBoolean());
        }
    }

    // ---- 7. POST /api/lab/reset ----

    [Fact]
    public async Task Post_lab_reset_returns_contract_and_preserves_experiment_results()
    {
        var client = _factory.CreateClient();
        SeedExperiment();

        var response = await client.PostAsync("/api/lab/reset", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertHas(doc.RootElement, "status", "productsSeeded", "clearedAtUtc");
        Assert.Equal(DemoCatalog.Products.Count,
            doc.RootElement.GetProperty("productsSeeded").GetInt32());

        // 实验结果表应被保留。
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        Assert.Equal(1, db.ExperimentResultRecords.Count());

        // M7 回归：reset 必须与启动种子同源，回填 8 个 SKU 且带分类，
        // 否则「重置演示数据」会把商城打回 2 个无分类商品。
        Assert.Equal(DemoCatalog.Products.Count, db.Products.Count());
        Assert.Equal(5, db.Products.Select(p => p.Category).Distinct().Count());
        Assert.All(db.Products, p => Assert.False(string.IsNullOrWhiteSpace(p.Category)));
    }

    // ---- 8. GET /api/experiments ----

    [Fact]
    public async Task Get_experiments_returns_result_array_with_json_string_fields()
    {
        var client = _factory.CreateClient();
        SeedExperiment("E3");

        var response = await client.GetAsync("/api/experiments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() >= 1);
        var first = doc.RootElement[0];
        AssertHas(first, "id", "name", "params", "result", "ranAtUtc", "passed");
        Assert.Equal(JsonValueKind.String, first.GetProperty("params").ValueKind);
        Assert.Equal(JsonValueKind.String, first.GetProperty("result").ValueKind);
    }

    // ---- 9. POST /api/experiments/run ----

    [Fact]
    public async Task Post_experiments_run_returns_single_result_and_rejects_unknown_name()
    {
        var client = _factory.CreateClient();

        var run = await client.PostAsJsonAsync("/api/experiments/run", new { name = "E1" });
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        using (var doc = JsonDocument.Parse(await run.Content.ReadAsStringAsync()))
        {
            var root = doc.RootElement;
            AssertHas(root, "id", "name", "params", "result", "ranAtUtc", "passed");
            Assert.Equal("E1", root.GetProperty("name").GetString());
            Assert.Equal(JsonValueKind.String, root.GetProperty("params").ValueKind);
            Assert.Equal(JsonValueKind.String, root.GetProperty("result").ValueKind);
        }

        var bad = await client.PostAsJsonAsync("/api/experiments/run", new { name = "BOGUS" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Post_experiments_run_E2_and_E3_also_persist_result_rows()
    {
        var client = _factory.CreateClient();

        foreach (var name in new[] { "E2", "E3" })
        {
            var run = await client.PostAsJsonAsync("/api/experiments/run", new { name });
            Assert.Equal(HttpStatusCode.OK, run.StatusCode);
            using var doc = JsonDocument.Parse(await run.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            AssertHas(root, "id", "name", "params", "result", "ranAtUtc", "passed");
            Assert.Equal(name, root.GetProperty("name").GetString());
            Assert.Equal(JsonValueKind.String, root.GetProperty("params").ValueKind);
            Assert.Equal(JsonValueKind.String, root.GetProperty("result").ValueKind);
        }

        // 两个实验都如实落行（passed 由真实断言产生，本进程无 broker 时为 false 也保留）。
        var all = await client.GetAsync("/api/experiments");
        using var allDoc = JsonDocument.Parse(await all.Content.ReadAsStringAsync());
        Assert.True(allDoc.RootElement.GetArrayLength() >= 2);
    }
}
