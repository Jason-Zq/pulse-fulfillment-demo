using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pulse.Infrastructure.Idempotency;
using Pulse.Infrastructure.Messaging;
using Xunit;

namespace Pulse.Infrastructure.Tests.M3;

public class MessageDispatcherTests
{
    private sealed class FakeDeduplicator : IEventDeduplicator
    {
        private readonly HashSet<string> _held = new();

        public List<string> Seen { get; } = new();
        public List<string> Released { get; } = new();
        public int Marked { get; private set; }
        public Func<string, DedupDecision>? Decision { get; set; }
        /// <summary>复刻 Redis SETNX：占用过的 key 未释放前判重。</summary>
        public bool Stateful { get; set; }

        public Task<DedupDecision> TryBeginAsync(
            string eventId,
            CancellationToken cancellationToken = default)
        {
            Seen.Add(eventId);
            if (Stateful)
            {
                return Task.FromResult(_held.Add(eventId)
                    ? DedupDecision.Proceed
                    : DedupDecision.Duplicate);
            }

            return Task.FromResult(Decision?.Invoke(eventId) ?? DedupDecision.Proceed);
        }

        /// <summary>释放失败投递占用的短钥匙，让重试消息能被真正处理。</summary>
        public Task ReleaseAsync(
            string eventId,
            CancellationToken cancellationToken = default)
        {
            Released.Add(eventId);
            _held.Remove(eventId);
            return Task.CompletedTask;
        }

        public Task MarkProcessedAsync(
            string eventId,
            CancellationToken cancellationToken = default)
        {
            Marked++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAck : IMessageAck
    {
        public List<ConsumeMessage> Acked { get; } = new();
        public List<(ConsumeMessage Message, string Error)> Retried { get; } = new();
        public List<(ConsumeMessage Message, string Error)> Dead { get; } = new();
        public Func<ConsumeMessage, Task>? OnAck { get; set; }

        public async Task AckAsync(ConsumeMessage message, CancellationToken cancellationToken = default)
        {
            Acked.Add(message);
            if (OnAck is not null)
            {
                await OnAck(message);
            }
        }

        public Task RequeueRetryAsync(
            ConsumeMessage message,
            string error,
            CancellationToken cancellationToken = default)
        {
            Retried.Add((message, error));
            return Task.CompletedTask;
        }

        public Task PublishDeadAsync(
            ConsumeMessage message,
            string error,
            CancellationToken cancellationToken = default)
        {
            Dead.Add((message, error));
            return Task.CompletedTask;
        }
    }

    private sealed class Recorder
    {
        public List<EventRoute> Routes { get; } = new();
        public List<OrderEvent> Events { get; } = new();
        public Func<OrderEvent, Task>? Behavior { get; set; }

        public Func<OrderEvent, CancellationToken, Task> Handler(EventRoute route) =>
            async (orderEvent, _) =>
            {
                Routes.Add(route);
                Events.Add(orderEvent);
                if (Behavior is not null)
                {
                    await Behavior(orderEvent);
                }
            };
    }

    private static ConsumeMessage Msg(
        string routingKey,
        string eventId = "evt-1",
        int failureCount = 0) =>
        new(eventId, routingKey,
            "{\"orderNo\":\"PO-1\",\"sku\":\"SKU-1\",\"quantity\":1}", failureCount);

    private static (OrderMessageDispatcher Dispatcher, FakeDeduplicator Dedup, FakeAck Ack, Recorder Recorder)
        CreateSut(
            Func<OrderEvent, Task>? behavior = null,
            Func<string, DedupDecision>? decision = null,
            Func<ConsumeMessage, Task>? onAck = null)
    {
        var dedup = new FakeDeduplicator { Decision = decision };
        var ack = new FakeAck { OnAck = onAck };
        var recorder = new Recorder { Behavior = behavior };
        var handlers = new Dictionary<EventRoute, Func<OrderEvent, CancellationToken, Task>>
        {
            [EventRoute.Fulfillment] = recorder.Handler(EventRoute.Fulfillment),
            [EventRoute.Timeout] = recorder.Handler(EventRoute.Timeout),
            [EventRoute.Retry] = recorder.Handler(EventRoute.Retry),
            [EventRoute.DeadLetter] = recorder.Handler(EventRoute.DeadLetter)
        };
        var dispatcher = new OrderMessageDispatcher(dedup, new RetryDecider(), ack, handlers);
        return (dispatcher, dedup, ack, recorder);
    }

    [Fact]
    public async Task Messages_are_dispatched_by_routing_key()
    {
        var (sut, _, ack, rec) = CreateSut();

        await sut.DispatchAsync(Msg(OrderTopology.RoutingKeyOrderPaid));
        await sut.DispatchAsync(Msg(OrderTopology.RoutingKeyOrderTimeout, "evt-2"));
        await sut.DispatchAsync(Msg(OrderTopology.RoutingKeyFulfillmentRetry, "evt-3"));
        await sut.DispatchAsync(Msg(OrderTopology.RoutingKeyDeadLetter, "evt-4"));

        Assert.Equal(
            new[] { EventRoute.Fulfillment, EventRoute.Timeout, EventRoute.Retry, EventRoute.DeadLetter },
            rec.Routes);
        Assert.Equal(4, ack.Acked.Count);
        Assert.Equal("evt-1", rec.Events[0].EventId);
        Assert.Equal(OrderTopology.RoutingKeyOrderPaid, rec.Events[0].RoutingKey);
        Assert.Equal("PO-1", rec.Events[0].OrderNo);
    }

    [Fact]
    public async Task Deduplicator_is_consulted_before_handler()
    {
        var (sut, dedup, ack, rec) = CreateSut(decision: _ => DedupDecision.Duplicate);

        await sut.DispatchAsync(Msg(OrderTopology.RoutingKeyOrderPaid));

        Assert.Equal(new[] { "evt-1" }, dedup.Seen);
        Assert.Empty(rec.Routes);
        Assert.Single(ack.Acked);
        Assert.Equal(0, dedup.Marked);
    }

    [Fact]
    public async Task Successful_message_is_marked_processed_and_acked()
    {
        var (sut, dedup, ack, _) = CreateSut();

        await sut.DispatchAsync(Msg(OrderTopology.RoutingKeyOrderPaid));

        Assert.Equal(1, dedup.Marked);
        Assert.Single(ack.Acked);
        Assert.Empty(ack.Retried);
        Assert.Empty(ack.Dead);
    }

    [Fact]
    public async Task Retriable_failure_is_requeued()
    {
        var (sut, _, ack, _) = CreateSut(
            behavior: _ => throw new InvalidOperationException("boom"));

        await sut.DispatchAsync(Msg(OrderTopology.RoutingKeyOrderPaid, failureCount: 0));

        Assert.Single(ack.Retried);
        Assert.Equal("boom", ack.Retried[0].Error);
        Assert.Empty(ack.Dead);
        Assert.Empty(ack.Acked);
    }

    [Fact]
    public async Task Failure_over_limit_is_published_dead()
    {
        var (sut, _, ack, _) = CreateSut(
            behavior: _ => throw new InvalidOperationException("boom"));

        await sut.DispatchAsync(Msg(OrderTopology.RoutingKeyOrderPaid, failureCount: 2));

        Assert.Single(ack.Dead);
        Assert.Empty(ack.Retried);
    }

    [Fact]
    public async Task Unknown_routing_key_is_acked_without_handler_or_dedup()
    {
        var (sut, dedup, ack, rec) = CreateSut();

        await sut.DispatchAsync(Msg("order.unknown"));

        Assert.Single(ack.Acked);
        Assert.Empty(rec.Routes);
        Assert.Empty(dedup.Seen);
    }

    [Fact]
    public async Task Single_failure_does_not_break_batch()
    {
        var (sut, _, ack, rec) = CreateSut(
            onAck: m => m.EventId == "evt-1"
                ? throw new InvalidOperationException("ack failed")
                : Task.CompletedTask);

        await sut.DispatchBatchAsync(new[]
        {
            Msg(OrderTopology.RoutingKeyOrderPaid, "evt-1"),
            Msg(OrderTopology.RoutingKeyOrderPaid, "evt-2"),
            Msg(OrderTopology.RoutingKeyOrderTimeout, "evt-3")
        });

        Assert.Equal(3, rec.Routes.Count);
        Assert.Contains(ack.Acked, m => m.EventId == "evt-2");
        Assert.Contains(ack.Acked, m => m.EventId == "evt-3");
    }

    [Fact]
    public async Task Duplicate_message_invokes_handler_only_once()
    {
        var seen = new HashSet<string>();
        var (sut, _, ack, rec) = CreateSut(
            decision: id => seen.Add(id) ? DedupDecision.Proceed : DedupDecision.Duplicate);

        await sut.DispatchAsync(Msg(OrderTopology.RoutingKeyOrderPaid));
        await sut.DispatchAsync(Msg(OrderTopology.RoutingKeyOrderPaid));

        Assert.Single(rec.Routes);
        Assert.Equal(2, ack.Acked.Count);
    }

    /// <summary>
    /// C 层 Docker 联调回归：失败消息若占着 dedup:{event_id} 不释放，
    /// 重试消息回来会被判重直接 ack，三次重试 / DLQ 闭环永远不成立。
    /// </summary>
    [Fact]
    public async Task Retriable_failure_releases_the_dedup_key_before_requeue()
    {
        var (sut, dedup, ack, _) = CreateSut(
            behavior: _ => throw new InvalidOperationException("boom"));

        await sut.DispatchAsync(Msg(OrderTopology.RoutingKeyOrderPaid, failureCount: 0));

        Assert.Single(ack.Retried);
        Assert.Equal(new[] { "evt-1" }, dedup.Released);
    }

    /// <summary>同一 event_id 连续两次失败投递都应真正执行业务处理器（不被误判重复）。</summary>
    [Fact]
    public async Task Failed_message_can_be_retried_with_the_same_event_id()
    {
        var dedup = new FakeDeduplicator { Stateful = true };
        var ack = new FakeAck();
        var recorder = new Recorder
        {
            Behavior = _ => throw new InvalidOperationException("boom")
        };
        var handlers = new Dictionary<EventRoute, Func<OrderEvent, CancellationToken, Task>>
        {
            [EventRoute.Fulfillment] = recorder.Handler(EventRoute.Fulfillment)
        };
        var sut = new OrderMessageDispatcher(dedup, new RetryDecider(), ack, handlers);
        var msg = Msg(OrderTopology.RoutingKeyOrderPaid, failureCount: 0);

        await sut.DispatchAsync(msg);
        await sut.DispatchAsync(msg);

        Assert.Equal(2, recorder.Routes.Count);
        Assert.Equal(2, ack.Retried.Count);
        Assert.Equal(new[] { "evt-1", "evt-1" }, dedup.Released);
    }

    [Fact]
    public void Consumer_host_is_registered_as_hosted_service()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddPulseMessaging(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.Contains(provider.GetServices<IHostedService>(), h => h is RabbitMqConsumerHost);
    }
}
