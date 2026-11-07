namespace Pulse.Infrastructure.Messaging;

/// <summary>失败后的处理决定。</summary>
public enum RetryDecision
{
    Retry,
    DeadLetter
}

/// <summary>重试决策端口（SPEC 第 6 节：三次失败进 DLQ）。</summary>
public interface IRetryDecider
{
    RetryDecision Decide(int failureCount);
    string RoutingKeyFor(RetryDecision decision);
}

public sealed class RetryDecider : IRetryDecider
{
    public RetryDecision Decide(int failureCount) =>
        failureCount < OrderTopology.FulfillmentMaxRetries
            ? RetryDecision.Retry
            : RetryDecision.DeadLetter;

    public string RoutingKeyFor(RetryDecision decision) => decision switch
    {
        RetryDecision.Retry => OrderTopology.RoutingKeyFulfillmentRetry,
        RetryDecision.DeadLetter => OrderTopology.RoutingKeyDeadLetter,
        _ => throw new ArgumentOutOfRangeException(nameof(decision))
    };
}
