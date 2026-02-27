using Donakunn.MessagingOverQueue.Topology.Abstractions;
using Donakunn.MessagingOverQueue.Topology.Builders;
using Donakunn.MessagingOverQueue.Topology.Conventions;

namespace Donakunn.MessagingOverQueue.Topology;

/// <summary>
/// Builds TopologyDefinition and HandlerRegistration from HandlerTopologyInfo.
/// </summary>
public sealed class HandlerTopologyBuilder(
    DefaultTopologyNamingConvention namingConvention,
    TopologyProviderOptions? options = null)
{
    private readonly DefaultTopologyNamingConvention _namingConvention =
        namingConvention ?? throw new ArgumentNullException(nameof(namingConvention));

    private readonly TopologyProviderOptions _options = options ?? new TopologyProviderOptions();

    public HandlerRegistration BuildHandlerRegistration(HandlerTopologyInfo handlerInfo)
    {
        ArgumentNullException.ThrowIfNull(handlerInfo);
        var topology = BuildTopologyDefinition(handlerInfo);

        return new HandlerRegistration
        {
            HandlerType        = handlerInfo.HandlerType,
            MessageType        = handlerInfo.MessageType,
            QueueName          = topology.StreamKey,
            ConsumerConfig     = handlerInfo.ConsumerQueueConfig,
            TopologyDefinition = topology
        };
    }

    public TopologyDefinition BuildTopologyDefinition(HandlerTopologyInfo handlerInfo)
    {
        ArgumentNullException.ThrowIfNull(handlerInfo);

        var names = _namingConvention.GetConsumerNames(
            handlerInfo.HandlerType,
            handlerInfo.MessageType);

        return new TopologyDefinition
        {
            MessageType       = handlerInfo.MessageType,
            StreamKey         = names.StreamKey,
            ConsumerGroupName = names.ConsumerGroupName,
            ConsumerConfig    = handlerInfo.ConsumerQueueConfig,
            DeadLetter        = BuildDeadLetterDefinition(names, handlerInfo.ConsumerQueueConfig)
        };
    }

    private DeadLetterDefinition? BuildDeadLetterDefinition(
        ConsumerTopologyNames names,
        ConsumerQueueInfo? consumerConfig)
    {
        if (consumerConfig?.QueueType == "stream")
            return null;

        if (!_options.EnableDeadLetterByDefault)
            return null;

        return new DeadLetterDefinition
        {
            StreamKey = _namingConvention.GetDeadLetterStreamKey(
                names.StreamKey,
                names.ConsumerGroupName)
        };
    }
}
