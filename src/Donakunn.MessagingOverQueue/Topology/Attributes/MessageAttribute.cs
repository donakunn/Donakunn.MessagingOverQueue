namespace Donakunn.MessagingOverQueue.Topology.Attributes;

/// <summary>
/// Marks a message type for auto-discovery by the topology scanner.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MessageAttribute : Attribute
{
    /// <summary>
    /// Whether to include this message in topology auto-discovery. Defaults to true.
    /// </summary>
    public bool AutoDiscover { get; init; } = true;

    /// <summary>
    /// Creates a new message attribute.
    /// </summary>
    public MessageAttribute() { }
}
