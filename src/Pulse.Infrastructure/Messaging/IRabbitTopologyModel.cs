namespace Pulse.Infrastructure.Messaging;

/// <summary>
/// 声明拓扑所需的窄端口。把 RabbitMQ.Client 的 IModel 隔离在适配器里，
/// 声明器本身可用内存 fake 做红绿测试，无需连接真 broker。
/// </summary>
public interface IRabbitTopologyModel
{
    void ExchangeDeclare(string exchange, string type, bool durable);

    void QueueDeclare(
        string queue,
        bool durable,
        IReadOnlyDictionary<string, object> arguments);

    void QueueBind(string queue, string exchange, string routingKey);
}
