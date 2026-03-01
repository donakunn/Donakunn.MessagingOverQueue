namespace Donakunn.MessagingOverQueue.Topology.Abstractions;

/// <summary>
/// Defines naming conventions for Redis Streams topology.
/// </summary>
public interface ITopologyNamingConvention
{
    /// <summary>
    /// Gets the stream key for a message type (publisher side).
    /// Derives category, name and optional version from [EventTopology] or convention.
    /// Formula: {category}.{name}[.{version}]
    /// </summary>
    string GetStreamKey(Type messageType);

    /// <summary>
    /// Gets stream key and consumer group name for a handler (consumer side).
    /// Stream key uses [EventTopology.Version] for versioning.
    /// Consumer group uses [ConsumerTopology.Category/Name] or ServiceName option or namespace.
    /// </summary>
    ConsumerTopologyNames GetConsumerNames(Type handlerType, Type messageType);

    /// <summary>
    /// Gets the dead letter stream key for a consumer group on a stream.
    /// Formula: {streamKey}:{consumerGroupName}:dlq
    /// </summary>
    string GetDeadLetterStreamKey(string streamKey, string consumerGroupName);
}

/// <summary>
/// Stream key and consumer group name for a handler registration.
/// </summary>
public record ConsumerTopologyNames(string StreamKey, string ConsumerGroupName);
