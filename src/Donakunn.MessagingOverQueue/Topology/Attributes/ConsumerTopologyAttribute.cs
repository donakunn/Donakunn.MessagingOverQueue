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

}
