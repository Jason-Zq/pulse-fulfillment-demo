using RabbitMQ.Client;

namespace Pulse.Infrastructure.Messaging;

/// <summary>
/// M5 5.5：用 RabbitMQ.Client 的 passive declare 读取 4 个队列的真实深度
/// （不引入 management HTTP API，避免 guest 远程登录 / 凭据配置）。
/// 复用 RabbitMqPublisher.CreateFactory 的连接串解析；懒连接（构造不连 broker）。
/// 未配置 / 连接不可用时返回 available:false 且深度 -1，不伪造 0。
/// </summary>
public sealed class RabbitQueueDepthProvider : IQueueDepthProvider
{
    private readonly RabbitMqOptions _options;

    public RabbitQueueDepthProvider(RabbitMqOptions options) => _options = options;

    public Task<QueueDepthSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return Task.FromResult(QueueDepthSnapshot.Unavailable());
        }

        try
        {
            var factory = RabbitMqPublisher.CreateFactory(_options);
            using var connection = factory.CreateConnection();
            using var model = connection.CreateModel();
            long fulfillment = model.QueueDeclarePassive(OrderTopology.QueueFulfillment).MessageCount;
            long orderTimeout = model.QueueDeclarePassive(OrderTopology.QueueOrderTimeout).MessageCount;
            long fulfillmentRetry = model.QueueDeclarePassive(OrderTopology.QueueFulfillmentRetry).MessageCount;
            long dlq = model.QueueDeclarePassive(OrderTopology.QueueDlqFulfillment).MessageCount;
            return Task.FromResult(new QueueDepthSnapshot(
                fulfillment, orderTimeout, fulfillmentRetry, dlq, Available: true));
        }
        catch
        {
            // 连接不可用 / 任一队列不存在：整体降级，不抛异常给仪表盘。
            return Task.FromResult(QueueDepthSnapshot.Unavailable());
        }
    }
}
