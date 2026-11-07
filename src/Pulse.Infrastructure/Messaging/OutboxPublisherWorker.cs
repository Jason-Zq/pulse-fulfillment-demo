using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pulse.Infrastructure.Lab;

namespace Pulse.Infrastructure.Messaging;

/// <summary>
/// 后台调度壳：轮询 outbox，把投递委托给可测的 <see cref="OutboxPublisher"/>。
/// 单轮异常只记日志、不退出进程，保证「停 MQ 不阻塞、恢复后补投」（A4）。
/// </summary>
public sealed class OutboxPublisherWorker : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxPublisherWorker> _logger;

    public OutboxPublisherWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxPublisherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var controls = scope.ServiceProvider.GetRequiredService<ILabControlStore>();

                // M5 5.4：publisher 开关 off 时跳过本轮（不发布），让 outbox 积压可见。
                if (await controls.IsEnabledAsync(LabControlKeys.Publisher, stoppingToken))
                {
                    var publisher = scope.ServiceProvider.GetRequiredService<OutboxPublisher>();
                    var summary = await publisher.PublishPendingAsync(BatchSize, stoppingToken);

                    if (summary.Attempted > 0)
                    {
                        _logger.LogInformation(
                            "outbox 投递: attempted={Attempted} published={Published} retried={Retried} failed={Failed}",
                            summary.Attempted,
                            summary.Published,
                            summary.Retried,
                            summary.Failed);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "outbox 投递轮次失败，下轮重试");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
