namespace Donakunn.MessagingOverQueue.Topology.Attributes;

/// <summary>
/// Overrides topology naming for a message handler (consumer).
/// Applied to handler types implementing IMessageHandler&lt;T&gt;.
/// Unset properties fall back to convention-based naming or EventTopology values.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ConsumerTopologyAttribute : Attribute
{
    /// <summary>
    /// Service name prefix in the queue name (e.g. "inventory").
    /// Falls back to global ServiceName option or namespace extraction when null.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Message name segment in the queue name.
    /// Falls back to EventTopology.Name or class name convention when null.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Version suffix for the stream key this consumer subscribes to.
    /// Overrides EventTopology.Version when set.
    /// Falls back to EventTopology.Version when null.
    /// Enables gradual migration: consumers can stay on v1 while publishers move to v2.
    /// </summary>
    public string? Version { get; init; }

}
