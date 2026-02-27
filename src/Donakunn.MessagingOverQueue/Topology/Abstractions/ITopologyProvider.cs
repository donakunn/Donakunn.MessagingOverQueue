namespace Donakunn.MessagingOverQueue.Topology.Abstractions;

/// <summary>
/// Provides topology configuration for a message type.
/// </summary>
public interface ITopologyProvider
{
    TopologyDefinition GetTopology(Type messageType);
    IReadOnlyCollection<TopologyDefinition> GetAllTopologies();
}

/// <summary>
/// Redis-Streams-native topology definition.
/// StreamKey is the Redis stream key (publisher writes, consumer reads).
/// ConsumerGroupName is the Redis consumer group (null for publisher-only definitions).
/// </summary>
public sealed class TopologyDefinition
{
    public Type MessageType { get; init; } = null!;

    /// <summary>Stream key formula: {category}.{name}[.{version}]</summary>
    public string StreamKey { get; init; } = string.Empty;

    /// <summary>Consumer group formula: {service}.{name} — null for publisher-only.</summary>
    public string? ConsumerGroupName { get; init; }

    public ConsumerQueueInfo? ConsumerConfig { get; init; }

    public DeadLetterDefinition? DeadLetter { get; init; }
}

/// <summary>
/// Dead letter stream configuration.
/// </summary>
public sealed class DeadLetterDefinition
{
    /// <summary>Dead letter stream key: {streamKey}:{consumerGroup}:dlq</summary>
    public string StreamKey { get; init; } = string.Empty;
}
