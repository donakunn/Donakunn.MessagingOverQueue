namespace Donakunn.MessagingOverQueue.Topology.Attributes;

/// <summary>
/// Overrides topology naming for an event type.
/// Applied to message types implementing IMessage.
/// Unset properties fall back to convention-based naming.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EventTopologyAttribute : Attribute
{
    /// <summary>
    /// Routing key category prefix (e.g. "orders").
    /// Falls back to namespace-based extraction when null.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Message name segment used in exchange name, routing key, and queue name.
    /// Falls back to class name minus known suffixes when null.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Version appended to the name with dot separator (e.g. "v2" produces "order-created.v2").
    /// No version suffix when null.
    /// </summary>
    public string? Version { get; init; }
}
