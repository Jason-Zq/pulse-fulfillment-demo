namespace Pulse.Infrastructure.Messaging;

/// <summary>
/// 订单链路的 RabbitMQ 拓扑（SPEC 第 6 节）：1 个 topic exchange + 4 个队列。
/// 超时关单用 TTL + DLX，不引入 rabbitmq_delayed_message_exchange 插件。
/// </summary>
public static class OrderTopology
{
    public const string ExchangeName = "ex.orders";

    public const string QueueFulfillment = "q.fulfillment";
    public const string QueueOrderTimeout = "q.order.timeout";
    public const string QueueFulfillmentRetry = "q.fulfillment.retry";
    public const string QueueDlqFulfillment = "q.dlq.fulfillment";

    public const string RoutingKeyOrderCreated = "order.created";
    public const string RoutingKeyOrderPaid = "order.paid";
    public const string RoutingKeyOrderTimeout = "order.timeout";
    public const string RoutingKeyFulfillmentRetry = "order.retry";
    public const string RoutingKeyDeadLetter = "order.dead";

    /// <summary>下单后 15 分钟未支付则关单。</summary>
    public const int OrderTimeoutTtlMs = 15 * 60 * 1000;

    /// <summary>履约失败重试间隔 5 秒。</summary>
    public const int FulfillmentRetryTtlMs = 5000;

    /// <summary>履约最大重试次数，超过后进入 DLQ。</summary>
    public const int FulfillmentMaxRetries = 3;

    public static TopologyDefinition Build()
    {
        var exchange = new ExchangeSpec(ExchangeName, "topic", Durable: true);

        var queues = new List<QueueSpec>
        {
            new(QueueFulfillment, Durable: true, EmptyArgs()),
            new(QueueOrderTimeout, Durable: true, new Dictionary<string, object>
            {
                ["x-message-ttl"] = OrderTimeoutTtlMs,
                ["x-dead-letter-exchange"] = ExchangeName,
                ["x-dead-letter-routing-key"] = RoutingKeyOrderTimeout
            }),
            new(QueueFulfillmentRetry, Durable: true, new Dictionary<string, object>
            {
                ["x-message-ttl"] = FulfillmentRetryTtlMs,
                ["x-dead-letter-exchange"] = ExchangeName,
                ["x-dead-letter-routing-key"] = RoutingKeyOrderPaid
            }),
            new(QueueDlqFulfillment, Durable: true, EmptyArgs())
        };

        var bindings = new List<BindingSpec>
        {
            new(QueueFulfillment, ExchangeName, RoutingKeyOrderPaid),
            new(QueueFulfillment, ExchangeName, RoutingKeyOrderTimeout),
            new(QueueOrderTimeout, ExchangeName, RoutingKeyOrderCreated),
            new(QueueFulfillmentRetry, ExchangeName, RoutingKeyFulfillmentRetry),
            new(QueueDlqFulfillment, ExchangeName, RoutingKeyDeadLetter)
        };

        return new TopologyDefinition(exchange, queues, bindings);
    }

    private static IReadOnlyDictionary<string, object> EmptyArgs() =>
        new Dictionary<string, object>();
}
