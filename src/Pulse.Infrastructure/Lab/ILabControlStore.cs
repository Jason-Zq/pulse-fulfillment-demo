namespace Pulse.Infrastructure.Lab;

/// <summary>运行态开关端口：默认 consumer/publisher 开、failure 注入关。</summary>
public interface ILabControlStore
{
    Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default);
    Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default);
}
