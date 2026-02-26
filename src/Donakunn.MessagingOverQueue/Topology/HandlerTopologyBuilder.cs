using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Topology.Abstractions;
using Donakunn.MessagingOverQueue.Topology.Builders;
using Donakunn.MessagingOverQueue.Topology.Conventions;

namespace Donakunn.MessagingOverQueue.Topology;

/// <summary>
/// Builds topology definitions from handler topology information.
/// Combines convention-based naming with handler-level consumer configuration.
/// </summary>
/// <remarks>
/// Creates a new instance with the specified naming convention and options.
/// </remarks>
public sealed class HandlerTopologyBuilder(
    DefaultTopologyNamingConvention namingConvention,
    TopologyProviderOptions? options = null)
{
    private readonly DefaultTopologyNamingConvention _namingConvention = namingConvention ?? throw new ArgumentNullException(nameof(namingConvention));
    private readonly TopologyProviderOptions _options = options ?? new TopologyProviderOptions();

    /// <summary>
    /// Builds a handler registration from handler topology info.
    /// </summary>
    public HandlerRegistration BuildHandlerRegistration(HandlerTopologyInfo handlerInfo)
    {
        ArgumentNullException.ThrowIfNull(handlerInfo);

        var topology = BuildTopologyDefinition(handlerInfo);
        var queueName = DetermineConsumerQueueName(topology);

        return new HandlerRegistration
        {
            HandlerType = handlerInfo.HandlerType,
            MessageType = handlerInfo.MessageType,
            QueueName = queueName,
            ConsumerConfig = handlerInfo.ConsumerQueueConfig,
            TopologyDefinition = topology
        };
    }

    /// <summary>
    /// Builds a topology definition from handler topology info.
    /// </summary>
    public TopologyDefinition BuildTopologyDefinition(HandlerTopologyInfo handlerInfo)
    {
        ArgumentNullException.ThrowIfNull(handlerInfo);

        var messageType = handlerInfo.MessageType;
        var handlerType = handlerInfo.HandlerType;

        // Build exchange from conventions
        var exchange = BuildExchangeDefinition(messageType);

        // Build queue - use handler's ConsumerQueueAttribute if present, otherwise convention
        var queue = BuildQueueDefinition(messageType, handlerType, handlerInfo.ConsumerQueueConfig);

        // Determine routing key
        var routingKey = _namingConvention.GetRoutingKey(messageType);

        // Build binding
        var binding = new BindingDefinition
        {
            ExchangeName = exchange.Name,
            QueueName = queue.Name,
            RoutingKey = routingKey
        };

        // Build dead letter configuration
        var deadLetter = BuildDeadLetterDefinition(queue.Name, handlerInfo.ConsumerQueueConfig);

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

    private ExchangeDefinition BuildExchangeDefinition(Type messageType)
    {
        var exchangeName = _namingConvention.GetExchangeName(messageType);
        var exchangeType = _namingConvention.GetExchangeType(messageType);

        return new ExchangeDefinition
        {
            Name = exchangeName,
            Type = exchangeType,
            Durable = _options.DefaultDurable,
            AutoDelete = false
        };
    }

    private QueueDefinition BuildQueueDefinition(
        Type messageType,
        Type handlerType,
        ConsumerQueueInfo? consumerConfig)
    {
        var queueName = _namingConvention.GetConsumerQueueName(handlerType, messageType);

        var arguments = new Dictionary<string, object>();

        var queueType = consumerConfig?.QueueType;
        if (queueType != null)
            arguments["x-queue-type"] = queueType;

        if (_options.EnableDeadLetterByDefault)
        {
            var dlxName = _namingConvention.GetDeadLetterExchangeName(queueName);
            arguments["x-dead-letter-exchange"] = dlxName;
        }

        return new QueueDefinition
        {
            Name = queueName,
            Durable = consumerConfig?.Durable ?? _options.DefaultDurable,
            Exclusive = consumerConfig?.Exclusive ?? false,
            AutoDelete = consumerConfig?.AutoDelete ?? false,
            MessageTtl = consumerConfig?.MessageTtlMs,
            MaxLength = consumerConfig?.MaxLength,
            MaxLengthBytes = consumerConfig?.MaxLengthBytes,
            QueueType = queueType,
            Arguments = arguments.Count > 0 ? arguments : null
        };
    }

    private DeadLetterDefinition? BuildDeadLetterDefinition(
        string sourceQueueName,
        ConsumerQueueInfo? consumerConfig)
    {
        // Disable dead letter for stream queues
        if (consumerConfig?.QueueType == "stream")
            return null;

        if (!_options.EnableDeadLetterByDefault)
            return null;

        return new DeadLetterDefinition
        {
            ExchangeName = _namingConvention.GetDeadLetterExchangeName(sourceQueueName),
            QueueName = _namingConvention.GetDeadLetterQueueName(sourceQueueName)
        };
    }

    private static string DetermineConsumerQueueName(TopologyDefinition topology)
        => topology.Queue.Name;

}
