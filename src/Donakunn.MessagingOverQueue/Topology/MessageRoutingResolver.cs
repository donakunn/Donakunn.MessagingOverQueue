using System.Collections.Concurrent;
using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Topology.Abstractions;

namespace Donakunn.MessagingOverQueue.Topology;

/// <summary>
/// Cached routing information for a message type.
/// </summary>
public sealed record RoutingInfo(string StreamKey)
{
    // Backward-compat shim used by RedisStreamsMessagePublisher
    public string QueueName => StreamKey;
    public string ExchangeName => string.Empty;
    public string RoutingKey => string.Empty;
}

/// <summary>
/// Resolves routing information for messages using the topology provider.
/// </summary>
public interface IMessageRoutingResolver
{
    RoutingInfo ResolveRouting<TMessage>() where TMessage : IMessage;
    RoutingInfo ResolveRouting(Type messageType);
    TopologyDefinition GetTopology<TMessage>() where TMessage : IMessage;
    TopologyDefinition GetTopology(Type messageType);
}

/// <summary>
/// Default implementation — caches one RoutingInfo per message type.
/// </summary>
public sealed class MessageRoutingResolver(ITopologyProvider topologyProvider)
    : IMessageRoutingResolver
{
    private readonly ITopologyProvider _topologyProvider =
        topologyProvider ?? throw new ArgumentNullException(nameof(topologyProvider));

    private readonly ConcurrentDictionary<Type, RoutingInfo> _cache = new();

    public RoutingInfo ResolveRouting<TMessage>() where TMessage : IMessage
        => ResolveRouting(typeof(TMessage));

    public RoutingInfo ResolveRouting(Type messageType)
        => _cache.GetOrAdd(messageType, t =>
        {
            var topo = _topologyProvider.GetTopology(t);
            return new RoutingInfo(topo.StreamKey);
        });

    public TopologyDefinition GetTopology<TMessage>() where TMessage : IMessage
        => GetTopology(typeof(TMessage));

    public TopologyDefinition GetTopology(Type messageType)
        => _topologyProvider.GetTopology(messageType);
}
