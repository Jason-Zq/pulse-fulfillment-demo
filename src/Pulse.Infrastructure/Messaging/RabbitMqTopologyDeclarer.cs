namespace Pulse.Infrastructure.Messaging;

/// <summary>把 <see cref="TopologyDefinition"/> 应用到 <see cref="IRabbitTopologyModel"/>。</summary>
public sealed class RabbitMqTopologyDeclarer
{
    public void Declare(IRabbitTopologyModel model, TopologyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(definition);

        model.ExchangeDeclare(
            definition.Exchange.Name,
            definition.Exchange.Type,
            definition.Exchange.Durable);

        foreach (var queue in definition.Queues)
        {
            model.QueueDeclare(queue.Name, queue.Durable, queue.Arguments);
        }

        foreach (var binding in definition.Bindings)
        {
            model.QueueBind(binding.Queue, binding.Exchange, binding.RoutingKey);
        }
    }

    public void Declare(IRabbitTopologyModel model) => Declare(model, OrderTopology.Build());
}
