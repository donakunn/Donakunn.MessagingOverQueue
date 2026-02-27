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

        var existing = _registry.GetTopology(messageType);
        if (existing != null)
            return existing;

        var definition = new TopologyDefinition
        {
            MessageType = messageType,
            StreamKey   = _namingConvention.GetStreamKey(messageType)
        };

        _registry.Register(definition);
        return definition;
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
