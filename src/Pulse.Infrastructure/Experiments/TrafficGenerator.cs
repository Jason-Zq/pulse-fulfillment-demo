using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Infrastructure.Orders;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Experiments;

/// <summary>POST /api/lab/traffic 请求（concurrency 缺省 1，兼容 SQLite）。</summary>
public sealed record TrafficRequest(
    string Sku,
    int Count,
    int Rps,
    int Quantity,
    int? Concurrency);

public sealed record TrafficMetrics(
    int Requested,
    int Created,
    int Failed,
    double ElapsedMs,
    double ThroughputRps,
    double P99Ms);

public sealed record TrafficResult(
    int Requested,
    int Created,
    int Failed,
    double ElapsedMs,
    double ThroughputRps,
    double P99Ms,
    long ExperimentId);

/// <summary>
/// M5 5.6：流量生成器。每单用独立 IServiceScopeFactory scope + 唯一 Guid 幂等键，
/// SemaphoreSlim 限并发（默认 concurrency=1 兼容 SQLite），逐单计时算吞吐 / P99。
/// rps 为「目标投放速率」：rps&gt;0 时按 index 摊薄启动时间；rps=0 表示不限速（实验 E3 用）。
/// </summary>
public sealed class TrafficGenerator
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TrafficGenerator(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    /// <summary>P99：排序后按 ceil(0.99*N)-1 取索引（不瞎写）。</summary>
    public static double P99(IEnumerable<double> samples)
    {
        var sorted = samples.OrderBy(x => x).ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(0.99 * sorted.Count) - 1;
        if (index < 0)
        {
            index = 0;
        }

        if (index >= sorted.Count)
        {
            index = sorted.Count - 1;
        }

        return sorted[index];
    }

    public static double ThroughputRps(int created, double elapsedMs) =>
        elapsedMs <= 0 ? 0 : created * 1000.0 / elapsedMs;

    /// <summary>核心生成逻辑：只算吞吐 / P99，不落实验行（供 E3 复用）。</summary>
    public async Task<TrafficMetrics> GenerateCoreAsync(
        TrafficRequest request,
        CancellationToken cancellationToken = default)
    {
        var concurrency = request.Concurrency.GetValueOrDefault(1);
        if (concurrency < 1)
        {
            concurrency = 1;
        }

        var count = Math.Max(0, request.Count);
        var latencies = new ConcurrentBag<double>();
        var created = 0;
        var failed = 0;
        using var semaphore = new SemaphoreSlim(concurrency);
        var wall = Stopwatch.StartNew();

        var tasks = new List<Task>(count);
        for (var i = 0; i < count; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                // 目标 rps：按序号摊薄启动，避免一次性打满；rps<=0 不限速。
                if (request.Rps > 0 && count > 1)
                {
                    var delayMs = 1000.0 * index / request.Rps;
                    if (delayMs > 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
                    }
                }

                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var place = scope.ServiceProvider.GetRequiredService<PlaceOrderService>();
                    var one = Stopwatch.StartNew();
                    await place.PlaceAsync(
                        new PlaceOrderCommand(
                            request.Sku,
                            request.Quantity,
                            Guid.NewGuid().ToString("N")),
                        cancellationToken);
                    one.Stop();
                    latencies.Add(one.Elapsed.TotalMilliseconds);
                    Interlocked.Increment(ref created);
                }
                catch
                {
                    // 单单失败（库存不足 / 并发冲突等）只计数，不影响其余订单。
                    Interlocked.Increment(ref failed);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        wall.Stop();

        var elapsedMs = wall.Elapsed.TotalMilliseconds;
        return new TrafficMetrics(
            count,
            created,
            failed,
            elapsedMs,
            ThroughputRps(created, elapsedMs),
            P99(latencies));
    }

    /// <summary>生成 + 落一条 TRAFFIC 实验行（params / result 为 JSON 字符串）。</summary>
    public async Task<TrafficResult> GenerateAsync(
        TrafficRequest request,
        CancellationToken cancellationToken = default)
    {
        var metrics = await GenerateCoreAsync(request, cancellationToken);
        var concurrency = request.Concurrency.GetValueOrDefault(1);
        var paramsJson = JsonSerializer.Serialize(new
        {
            sku = request.Sku,
            count = request.Count,
            rps = request.Rps,
            quantity = request.Quantity,
            concurrency
        });
        var resultJson = JsonSerializer.Serialize(new
        {
            requested = metrics.Requested,
            created = metrics.Created,
            failed = metrics.Failed,
            elapsedMs = metrics.ElapsedMs,
            throughputRps = metrics.ThroughputRps,
            p99Ms = metrics.P99Ms
        });

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        var row = new ExperimentResultRecord
        {
            Name = "TRAFFIC",
            Params = paramsJson,
            Result = resultJson,
            RanAtUtc = DateTime.UtcNow,
            Passed = metrics.Failed == 0 && metrics.Created == metrics.Requested
        };
        db.ExperimentResultRecords.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        return new TrafficResult(
            metrics.Requested,
            metrics.Created,
            metrics.Failed,
            metrics.ElapsedMs,
            metrics.ThroughputRps,
            metrics.P99Ms,
            row.Id);
    }
}
