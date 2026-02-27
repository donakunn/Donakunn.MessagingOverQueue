using Donakunn.MessagingOverQueue.Configuration.Options;

namespace Donakunn.MessagingOverQueue.Hosting;

/// <summary>
/// Registration for a consumer, created during topology configuration.
/// Options.QueueName holds the stream key; ConsumerGroupName holds the consumer group.
/// </summary>
public sealed class ConsumerRegistration
{
    public ConsumerOptions Options { get; init; } = new();
    public Type? HandlerType { get; init; }
    public string? ConsumerGroupName { get; init; }
}
