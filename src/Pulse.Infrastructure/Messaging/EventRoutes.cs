namespace Pulse.Infrastructure.Messaging;

/// <summary>消费端的处理路线（由 routing key 解析得到）。</summary>
public enum EventRoute
{
    Fulfillment,
    Timeout,
    Retry,
    DeadLetter,
    Unsupported
}

/// <summary>routing key → 处理路线的纯数据分派表（不依赖 broker）。</summary>
public static class EventRoutes
{
    public static EventRoute Resolve(string routingKey) => routingKey switch
    {
        OrderTopology.RoutingKeyOrderPaid => EventRoute.Fulfillment,
        OrderTopology.RoutingKeyOrderTimeout => EventRoute.Timeout,
        OrderTopology.RoutingKeyFulfillmentRetry => EventRoute.Retry,
        OrderTopology.RoutingKeyDeadLetter => EventRoute.DeadLetter,
        _ => EventRoute.Unsupported
    };
}
