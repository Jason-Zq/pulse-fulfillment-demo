using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Spi;

namespace Pulse.Worker;

/// <summary>
/// M5 5.2：从 IServiceProvider（实际是每次触发的 DI scope）解析 Job 的 IJobFactory。
/// Job 与其依赖（DailyReportWriter / PulseDbContext）注册为 Scoped，
/// 因此 NewJob 时新建 scope，ReturnJob 时释放，避免从根容器解析 Scoped 服务。
/// </summary>
public sealed class DiJobFactory : IJobFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<IJob, IServiceScope> _scopes = new();

    public DiJobFactory(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        var scope = _scopeFactory.CreateScope();
        var job = (IJob)scope.ServiceProvider.GetRequiredService(bundle.JobDetail.JobType);
        _scopes[job] = scope;
        return job;
    }

    public void ReturnJob(IJob job)
    {
        if (_scopes.TryRemove(job, out var scope))
        {
            scope.Dispose();
        }
    }
}
