using Donakunn.MessagingOverQueue.Configuration.Options;

namespace Donakunn.MessagingOverQueue.Hosting;

/// <summary>
/// Registration for a consumer, created during topology configuration.
/// </summary>
public sealed class ConsumerRegistration
{
    /// <summary>
    /// The consumer options (queue name, prefetch, concurrency).
    /// </summary>
    public ConsumerOptions Options { get; init; } = new();

    /// <summary>
    /// The handler type associated with this consumer (for diagnostics).
    /// </summary>
    public Type? HandlerType { get; init; }

    /// <summary>
    /// The exchange name for routing (used by some providers like Redis Streams).
    /// </summary>
    public string? ExchangeName { get; init; }

    /// <summary>
    /// The routing key for routing (used by some providers like Redis Streams).
    /// </summary>
    public string? RoutingKey { get; init; }
}
