using RabbitMQ.Client;

namespace Pulse.Infrastructure.Messaging;

/// <summary>把窄端口 <see cref="IRabbitTopologyModel"/> 映射到 RabbitMQ.Client 的 IModel。</summary>
public sealed class RabbitModelAdapter : IRabbitTopologyModel
{
    private readonly IModel _model;

    public RabbitModelAdapter(IModel model) => _model = model;

    public void ExchangeDeclare(string exchange, string type, bool durable) =>
        _model.ExchangeDeclare(exchange, type, durable, autoDelete: false, arguments: null);

    public void QueueDeclare(
        string queue,
        bool durable,
        IReadOnlyDictionary<string, object> arguments) =>
        _model.QueueDeclare(
            queue,
            durable,
            exclusive: false,
            autoDelete: false,
            arguments: arguments.ToDictionary(k => k.Key, v => v.Value));

    public void QueueBind(string queue, string exchange, string routingKey) =>
        _model.QueueBind(queue, exchange, routingKey, arguments: null);
}
