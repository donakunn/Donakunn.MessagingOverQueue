using Donakunn.MessagingOverQueue.Configuration.Options;

namespace Donakunn.MessagingOverQueue.Hosting;

/// <summary>
/// Registration for a consumer, created during topology configuration.
/// Options.QueueName holds the stream key; ConsumerGroupName holds the consumer group.
/// HandlerTypes lists all handler types bound to this specific stream (queue).
/// When multiple handlers share the same stream (fan-out), all are listed.
/// When a handler has a [ConsumerTopology] version override, only that handler is listed.
/// </summary>
public sealed class ConsumerRegistration
{
    public ConsumerOptions Options { get; init; } = new();
    public Type? HandlerType { get; init; }
    public IReadOnlyList<Type> HandlerTypes { get; init; } = [];
    public string? ConsumerGroupName { get; init; }
}
