using Donakunn.MessagingOverQueue.Topology.Abstractions;
using Donakunn.MessagingOverQueue.Topology.Builders;

namespace Donakunn.MessagingOverQueue.Topology;

/// <summary>
/// Publisher-side topology provider. Builds TopologyDefinition from [EventTopology] attributes.
/// ConsumerGroupName is null — this definition is for routing/publishing only.
/// </summary>
public sealed class ConventionBasedTopologyProvider(
    ITopologyNamingConvention namingConvention,
    ITopologyRegistry registry,
    TopologyProviderOptions? options = null) : ITopologyProvider
{
    private readonly ITopologyNamingConvention _namingConvention =
        namingConvention ?? throw new ArgumentNullException(nameof(namingConvention));

    private readonly ITopologyRegistry _registry =
        registry ?? throw new ArgumentNullException(nameof(registry));

    public TopologyDefinition GetTopology(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        // Always compute stream key from the event's [EventTopology] attribute.
        // Do NOT read consumer-registered topologies from the registry: a handler with
        // [ConsumerTopology(Version="v2")] has its OWN consumer stream, but the publisher
        // must still write to the event's canonical stream (v1).
        return new TopologyDefinition
        {
            MessageType = messageType,
            StreamKey   = _namingConvention.GetStreamKey(messageType)
        };
    }

    public IReadOnlyCollection<TopologyDefinition> GetAllTopologies()
        => _registry.GetAllTopologies();
}

/// <summary>
/// Options for topology provider behavior.
/// </summary>
public sealed class TopologyProviderOptions
{
    /// <summary>
    /// Whether to enable dead letter queues by default. Defaults to true.
    /// </summary>
    public bool EnableDeadLetterByDefault { get; set; } = true;
}
