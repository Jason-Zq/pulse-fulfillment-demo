using Pulse.Infrastructure.Reports;
using Quartz;

namespace Pulse.Worker;

/// <summary>
/// M5 5.2：Quartz 定时任务，落「昨日 UTC」的日报（复用 DailyReportWriter 的 upsert 幂等口径）。
/// 由 <see cref="DiJobFactory"/> 从 DI scope 解析，可注入 Writer / DbContext。
/// </summary>
public sealed class DailyReportJob : IJob
{
    public static readonly JobKey Key = new("DailyReportJob", "pulse-reports");

    private readonly DailyReportWriter _writer;
    private readonly ILogger<DailyReportJob> _logger;

    public DailyReportJob(DailyReportWriter writer, ILogger<DailyReportJob> logger)
    {
        _writer = writer;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var statDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        await _writer.UpsertAsync(statDate, context.CancellationToken);
        _logger.LogInformation("日报持久化完成: statDate={StatDate}", statDate);
    }
}
