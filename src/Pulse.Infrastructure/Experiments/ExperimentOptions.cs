using Microsoft.Extensions.Configuration;

namespace Pulse.Infrastructure.Experiments;

/// <summary>
/// M5 5.6：实验样本量与超时上限（可配置，写清默认值）。
/// 集成测试通过 Experiments:* 覆盖为小样本 / 短超时，避免请求挂死。
/// </summary>
public sealed class ExperimentOptions
{
    /// <summary>E1 造单数，默认 20。</summary>
    public int E1OrderCount { get; set; } = 20;

    /// <summary>E1 轮询 pending=0 的超时（秒），默认 60。</summary>
    public int E1PollTimeoutSeconds { get; set; } = 60;

    /// <summary>E2 同 event_id 重复投递条数，默认 1000。</summary>
    public int E2DuplicateCount { get; set; } = 1000;

    /// <summary>E2 等待消费的超时（秒），默认 30。</summary>
    public int E2ConsumeTimeoutSeconds { get; set; } = 30;

    /// <summary>E3 并发造单数，默认 100。</summary>
    public int E3OrderCount { get; set; } = 100;

    /// <summary>E3 并发度，默认 10。</summary>
    public int E3Concurrency { get; set; } = 10;

    /// <summary>E3 每单数量，默认 1。</summary>
    public int E3Quantity { get; set; } = 1;

    /// <summary>单次实验整体上限（秒），默认 90（&gt; E1 60 + E2 30 之外留缓冲）。</summary>
    public int RunTimeoutSeconds { get; set; } = 90;

    public static ExperimentOptions Load(IConfiguration configuration)
    {
        static int Get(IConfiguration c, string key, int fallback) =>
            int.TryParse(c["Experiments:" + key], out var value) && value > 0
                ? value
                : fallback;

        return new ExperimentOptions
        {
            E1OrderCount = Get(configuration, nameof(E1OrderCount), 20),
            E1PollTimeoutSeconds = Get(configuration, nameof(E1PollTimeoutSeconds), 60),
            E2DuplicateCount = Get(configuration, nameof(E2DuplicateCount), 1000),
            E2ConsumeTimeoutSeconds = Get(configuration, nameof(E2ConsumeTimeoutSeconds), 30),
            E3OrderCount = Get(configuration, nameof(E3OrderCount), 100),
            E3Concurrency = Get(configuration, nameof(E3Concurrency), 10),
            E3Quantity = Get(configuration, nameof(E3Quantity), 1),
            RunTimeoutSeconds = Get(configuration, nameof(RunTimeoutSeconds), 90)
        };
    }
}
