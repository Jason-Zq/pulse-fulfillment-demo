using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pulse.Infrastructure.Reports;
using Quartz;
using Quartz.Impl;

namespace Pulse.Worker;

/// <summary>
/// M5 5.2：用 Quartz 核心包托管「每日 01:00（Asia/Shanghai）落昨日 UTC 日报」的定时任务。
/// M10：cron 改为可配置（Reports__DailyCron），**默认仍是 0 0 1 * * ? 一字不变**；
/// 该配置项只用于本机真验证「调度器到点自动触发」，验证完必须恢复默认。
///
/// 【有意偏移 / 已记录】SPEC 第 5/9 节写的 ADO.NET JobStore 与 qrtz_* 官方表本里程碑不落：
///  - StdSchedulerFactory 默认 RAMJobStore，不引入 Quartz.Extensions.Hosting，也不引入
///    MySqlConnector / Microsoft.Data.Sqlite 的 JobStore DDL；
///  - 本机无法对 MySQL JobStore 做完整验证，且单实例演示站靠 daily_reports.stat_date
///    唯一键 upsert 已保证重跑幂等，进程重启后 Quartz 会重新调度（无持久化触发状态）。
/// 该偏移由主会话写入 docs/BOUNDARIES.md 未实现清单，不写成「已实现」。
/// </summary>
public sealed class QuartzDailyReportHost : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<QuartzDailyReportHost> _logger;
    private IScheduler? _scheduler;

    public QuartzDailyReportHost(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<QuartzDailyReportHost> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // RAMJobStore：仅进程内调度，不落 qrtz_* 表。
        var factory = new StdSchedulerFactory();
        _scheduler = await factory.GetScheduler(cancellationToken);
        _scheduler.JobFactory = new DiJobFactory(_scopeFactory);
        await _scheduler.Start(cancellationToken);

        var cron = DailyReportCron.Resolve(_configuration["Reports:DailyCron"]);

        var job = JobBuilder.Create<DailyReportJob>()
            .WithIdentity(DailyReportJob.Key)
            .Build();
        var trigger = TriggerBuilder.Create()
            .WithIdentity("daily-report-trigger", "pulse-reports")
            .WithSchedule(CronScheduleBuilder.CronSchedule(cron)
                .InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")))
            .Build();
        await _scheduler.ScheduleJob(job, trigger, cancellationToken);
        _logger.LogInformation(
            "Quartz 日报调度已启动: cron={Cron} timezone=Asia/Shanghai (默认 {DefaultCron})",
            cron, DailyReportCron.DefaultCron);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_scheduler is not null)
        {
            await _scheduler.Shutdown(cancellationToken);
        }
    }
}
