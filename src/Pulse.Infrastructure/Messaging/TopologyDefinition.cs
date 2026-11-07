namespace Pulse.Infrastructure.Messaging;

/// <summary>声明一个 exchange 所需的最小信息。</summary>
public sealed record ExchangeSpec(string Name, string Type, bool Durable);

/// <summary>声明一个队列所需的最小信息；Arguments 为 RabbitMQ 队列参数（TTL / DLX 等）。</summary>
public sealed record QueueSpec(
    string Name,
    bool Durable,
    IReadOnlyDictionary<string, object> Arguments);

/// <summary>把队列按 routing key 绑定到 exchange。</summary>
public sealed record BindingSpec(string Queue, string Exchange, string RoutingKey);

/// <summary>拓扑的纯数据描述，不依赖 RabbitMQ.Client，便于本地声明与测试。</summary>
public sealed record TopologyDefinition(
    ExchangeSpec Exchange,
    IReadOnlyList<QueueSpec> Queues,
    IReadOnlyList<BindingSpec> Bindings);
