using System.Text;
using RabbitMQ.Client;

namespace Pulse.Infrastructure.Messaging;

/// <summary>
/// 真实投递端：RabbitMQ.Client + publisher confirms（SPEC 第 6 节）。
/// 连接串一律来自 RabbitMqOptions（配置），构造时不连 broker（懒连接），
/// 未配置时返回 Confirmed=false 而不是抛异常，便于「停 MQ 不阻塞、恢复后补投」。
/// </summary>
public sealed class RabbitMqPublisher : IMessagePublisher, IDisposable
{
    private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(5);

    private readonly RabbitMqOptions _options;
    private readonly object _sync = new();
    private IConnection? _connection;
    private IModel? _model;

    public RabbitMqPublisher(RabbitMqOptions options) => _options = options;

    public Task<PublishResult> PublishAsync(
        PublishRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return Task.FromResult(new PublishResult(false, "RabbitMQ 连接串未配置"));
        }

        try
        {
            var model = EnsureModel();
            lock (_sync)
            {
                var body = Encoding.UTF8.GetBytes(request.Payload);
                var properties = model.CreateBasicProperties();
                properties.Persistent = true;
                properties.MessageId = request.EventId;
                model.BasicPublish(OrderTopology.ExchangeName, request.RoutingKey, properties, body);
                model.WaitForConfirmsOrDie(ConfirmTimeout);
            }

            return Task.FromResult(new PublishResult(true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new PublishResult(false, ex.Message));
        }
    }

    private IModel EnsureModel()
    {
        lock (_sync)
        {
            if (_model is null)
            {
                var factory = CreateFactory(_options);
                _connection = factory.CreateConnection();
                _model = _connection.CreateModel();
                _model.ConfirmSelect();
                // 首次连接后声明拓扑（ex.orders 与 4 个队列），幂等、不依赖插件。
                new RabbitMqTopologyDeclarer().Declare(new RabbitModelAdapter(_model));
            }

            return _model;
        }
    }

    internal static ConnectionFactory CreateFactory(RabbitMqOptions options)
    {
        var factory = new ConnectionFactory();
        var connectionString = options.ConnectionString;

        if (connectionString.StartsWith("amqp://", StringComparison.OrdinalIgnoreCase)
            || connectionString.StartsWith("amqps://", StringComparison.OrdinalIgnoreCase))
        {
            factory.Uri = new Uri(connectionString);
            return factory;
        }

        // 兼容 compose 中 "host=rabbitmq;username=..." 这类 key=value 连接串。
        foreach (var segment in connectionString.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = segment[..separator].Trim().ToLowerInvariant();
            var value = segment[(separator + 1)..].Trim();
            switch (key)
            {
                case "host":
                case "hostname":
                    factory.HostName = value;
                    break;
                case "username":
                case "user":
                    factory.UserName = value;
                    break;
                case "password":
                    factory.Password = value;
                    break;
                case "virtualhost":
                case "vhost":
                    factory.VirtualHost = value;
                    break;
                case "port" when int.TryParse(value, out var port):
                    factory.Port = port;
                    break;
            }
        }

        return factory;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _model?.Dispose();
            _connection?.Dispose();
            _model = null;
            _connection = null;
        }
    }
}
