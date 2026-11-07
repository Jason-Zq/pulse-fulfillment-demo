using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pulse.Infrastructure.Idempotency;
using Pulse.Infrastructure.Lab;
using Pulse.Infrastructure.Orders;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Pulse.Infrastructure.Messaging;

/// <summary>
/// 真实 RabbitMQ 消费者宿主（仅编译验证，未对真 broker 验证）：
/// EventingBasicConsumer + 手动 ack + prefetchCount=10。
/// 懒连接（配置缺失不连 broker、不阻塞启动）、单轮异常不退出（复刻 OutboxPublisherWorker）。
/// </summary>
public sealed class RabbitMqConsumerHost : BackgroundService
{
    private const ushort PrefetchCount = 10;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly RabbitMqOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RabbitMqConsumerHost> _logger;
    private readonly object _sync = new();
    private IConnection? _connection;
    private IModel? _model;

    public RabbitMqConsumerHost(
        RabbitMqOptions options,
        IServiceScopeFactory scopeFactory,
        ILogger<RabbitMqConsumerHost> logger)
    {
        _options = options;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
                {
                    EnsureConsumer(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ 消费者轮次失败，下轮重试");
            }

            try
            {
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void EnsureConsumer(CancellationToken stoppingToken)
    {
        lock (_sync)
        {
            if (_model is not null)
            {
                return;
            }

            // 复用 M2 RabbitMqPublisher 的连接串解析（amqp:// 或 key=value 形式）。
            var factory = RabbitMqPublisher.CreateFactory(_options);
            _connection = factory.CreateConnection();
            _model = _connection.CreateModel();
            _model.BasicQos(0, PrefetchCount, false);
            new RabbitMqTopologyDeclarer().Declare(new RabbitModelAdapter(_model));

            foreach (var queue in new[]
                     {
                         OrderTopology.QueueFulfillment,
                         OrderTopology.QueueDlqFulfillment
                     })
            {
                var consumer = new EventingBasicConsumer(_model);
                consumer.Received += (_, ea) => OnReceived(ea, stoppingToken);
                _model.BasicConsume(queue, autoAck: false, consumer);
            }
        }
    }

    /// <summary>
    /// 事件处理器是同步回调；桥接到 async 核心，核心内部 try/catch 保证单条异常不退出宿主。
    /// </summary>
    private void OnReceived(BasicDeliverEventArgs ea, CancellationToken cancellationToken) =>
        OnReceivedAsync(ea, cancellationToken).GetAwaiter().GetResult();

    private async Task OnReceivedAsync(BasicDeliverEventArgs ea, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var controls = services.GetRequiredService<ILabControlStore>();

            // M5 5.4：consumer 开关 off → BasicNack(requeue:true) + 约 200ms 短延时，
            // 让队列深度可见地增长，不 hot spin；消息稍后重新投递（不丢）。
            if (!await controls.IsEnabledAsync(LabControlKeys.Consumer, cancellationToken))
            {
                _model!.BasicNack(ea.DeliveryTag, false, requeue: true);
                await Task.Delay(TimeSpan.FromMilliseconds(200));
                return;
            }

            var message = new ConsumeMessage(
                ea.BasicProperties?.MessageId ?? Guid.NewGuid().ToString("N"),
                ea.RoutingKey,
                Encoding.UTF8.GetString(ea.Body.ToArray()),
                ReadFailureCount(ea));

            var ack = new RabbitMessageAck(
                _model!, ea.DeliveryTag,
                services.GetRequiredService<DeadLetterService>());
            var dispatcher = new OrderMessageDispatcher(
                services.GetRequiredService<IEventDeduplicator>(),
                services.GetRequiredService<IRetryDecider>(),
                ack,
                BuildHandlers(services, controls));

            await dispatcher.DispatchAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理单条消息失败，重新入队");
            try
            {
                _model?.BasicNack(ea.DeliveryTag, false, requeue: true);
            }
            catch
            {
                // 模型已关闭：仅记日志，不让单条异常冒泡退出宿主。
            }
        }
    }

    private static int ReadFailureCount(BasicDeliverEventArgs ea)
    {
        if (ea.BasicProperties?.Headers is not null
            && ea.BasicProperties.Headers.TryGetValue("x-retry-count", out var value)
            && value is int count)
        {
            return count;
        }

        return 0;
    }

    private static IReadOnlyDictionary<EventRoute, Func<OrderEvent, CancellationToken, Task>>
        BuildHandlers(IServiceProvider services, ILabControlStore controls)
    {
        var fulfillment = services.GetRequiredService<FulfillmentService>();
        var timeout = services.GetRequiredService<TimeoutOrderService>();
        return new Dictionary<EventRoute, Func<OrderEvent, CancellationToken, Task>>
        {
            [EventRoute.Fulfillment] = async (orderEvent, ct) =>
            {
                // M5 5.4：failure 注入 on 时抛异常，交给 OrderMessageDispatcher
                // 的 RetryDecider 走 3 次重试入 DLQ（不绕过 dispatcher）。
                if (await controls.IsEnabledAsync(LabControlKeys.Failure, ct))
                {
                    throw new InvalidOperationException("故障注入：模拟履约失败");
                }

                await fulfillment.FulfillAsync(orderEvent, ct);
            },
            [EventRoute.Timeout] = async (orderEvent, ct) =>
            {
                await timeout.CloseExpiredAsync(orderEvent, ct);
            }
        };
    }

    public override void Dispose()
    {
        lock (_sync)
        {
            _model?.Dispose();
            _connection?.Dispose();
            _model = null;
            _connection = null;
        }

        base.Dispose();
    }
}

/// <summary>手动 ack 的真实实现：把 dispatcher 的决定映射到 RabbitMQ 动作并落死信。</summary>
internal sealed class RabbitMessageAck : IMessageAck
{
    private readonly IModel _model;
    private readonly ulong _deliveryTag;
    private readonly DeadLetterService _deadLetters;

    public RabbitMessageAck(IModel model, ulong deliveryTag, DeadLetterService deadLetters)
    {
        _model = model;
        _deliveryTag = deliveryTag;
        _deadLetters = deadLetters;
    }

    public Task AckAsync(ConsumeMessage message, CancellationToken cancellationToken = default)
    {
        _model.BasicAck(_deliveryTag, multiple: false);
        return Task.CompletedTask;
    }

    public Task RequeueRetryAsync(
        ConsumeMessage message,
        string error,
        CancellationToken cancellationToken = default)
    {
        var properties = _model.CreateBasicProperties();
        properties.Persistent = true;
        properties.MessageId = message.EventId;
        properties.Headers = new Dictionary<string, object>
        {
            ["x-retry-count"] = message.FailureCount + 1
        };
        _model.BasicPublish(
            OrderTopology.ExchangeName,
            OrderTopology.RoutingKeyFulfillmentRetry,
            properties,
            Encoding.UTF8.GetBytes(message.Payload));
        _model.BasicAck(_deliveryTag, multiple: false);
        return Task.CompletedTask;
    }

    public async Task PublishDeadAsync(
        ConsumeMessage message,
        string error,
        CancellationToken cancellationToken = default)
    {
        await _deadLetters.RecordAsync(
            new DeadLetterRecord(
                message.EventId,
                message.Payload,
                error,
                message.FailureCount + 1),
            cancellationToken);

        var properties = _model.CreateBasicProperties();
        properties.Persistent = true;
        properties.MessageId = message.EventId;
        _model.BasicPublish(
            OrderTopology.ExchangeName,
            OrderTopology.RoutingKeyDeadLetter,
            properties,
            Encoding.UTF8.GetBytes(message.Payload));
        _model.BasicAck(_deliveryTag, multiple: false);
    }
}
