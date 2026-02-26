using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Topology.Abstractions;
using Donakunn.MessagingOverQueue.Topology.Builders;

namespace Donakunn.MessagingOverQueue.Topology;

/// <summary>
/// Convention-based topology provider that uses naming conventions to build topology.
/// </summary>
/// <remarks>
/// Creates a new instance with the specified dependencies.
/// </remarks>
public sealed class ConventionBasedTopologyProvider(
    ITopologyNamingConvention namingConvention,
    ITopologyRegistry registry,
    TopologyProviderOptions? options = null) : ITopologyProvider
{
    private readonly ITopologyNamingConvention _namingConvention = namingConvention ?? throw new ArgumentNullException(nameof(namingConvention));
    private readonly ITopologyRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly TopologyProviderOptions _options = options ?? new TopologyProviderOptions();

    /// <inheritdoc />
    public TopologyDefinition GetTopology(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        // Check registry first for cached/pre-configured definitions
        var existing = _registry.GetTopology(messageType);
        if (existing != null)
            return existing;

        // Build topology from conventions
        var definition = BuildTopologyDefinition(messageType);

        // Cache in registry
        _registry.Register(definition);

        return definition;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<TopologyDefinition> GetAllTopologies()
    {
        return _registry.GetAllTopologies();
    }

    private TopologyDefinition BuildTopologyDefinition(Type messageType)
    {
        var exchangeName = _namingConvention.GetExchangeName(messageType);
        var exchangeType = _namingConvention.GetExchangeType(messageType);

        var exchange = new ExchangeDefinition
        {
            Name = exchangeName,
            Type = exchangeType,
            Durable = _options.DefaultDurable,
            AutoDelete = false
        };

        var queueName = _namingConvention.GetQueueName(messageType);

        var queue = new QueueDefinition
        {
            Name = queueName,
            Durable = _options.DefaultDurable,
            Exclusive = false,
            AutoDelete = false
        };

        var routingKey = _namingConvention.GetRoutingKey(messageType);

        var binding = new BindingDefinition
        {
            ExchangeName = exchange.Name,
            QueueName = queue.Name,
            RoutingKey = routingKey
        };

        var deadLetter = _options.EnableDeadLetterByDefault
            ? new DeadLetterDefinition
            {
                ExchangeName = _namingConvention.GetDeadLetterExchangeName(queueName),
                QueueName = _namingConvention.GetDeadLetterQueueName(queueName)
            }
            : null;

        return new TopologyDefinition
        {
            MessageType = messageType,
            Exchange = exchange,
            Queue = queue,
            Binding = binding,
            DeadLetter = deadLetter,
            RoutingKey = routingKey
        };
    }

}

/// <summary>
/// Options for topology provider behavior.
/// </summary>
public sealed class TopologyProviderOptions
{
    /// <summary>
    /// Whether exchanges and queues are durable by default. Defaults to true.
    /// </summary>
    public bool DefaultDurable { get; set; } = true;

    /// <summary>
    /// Whether to enable dead letter queues by default. Defaults to true.
    /// </summary>
    public bool EnableDeadLetterByDefault { get; set; } = true;
}
