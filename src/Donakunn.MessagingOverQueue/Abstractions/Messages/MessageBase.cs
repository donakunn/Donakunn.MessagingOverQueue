using System.Collections.Concurrent;

namespace Donakunn.MessagingOverQueue.Abstractions.Messages;

/// <summary>
/// Abstract base record for all messages providing common functionality.
/// </summary>
public abstract record MessageBase : IMessage
{
    private static readonly ConcurrentDictionary<Type, string> MessageTypeNameCache = new();

    /// <inheritdoc />
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <inheritdoc />
    public string? CorrelationId { get; init; }

    /// <inheritdoc />
    public string? CausationId { get; init; }

    /// <inheritdoc />
    public virtual string MessageType => MessageTypeNameCache.GetOrAdd(
        GetType(),
        type => type.AssemblyQualifiedName ?? type.FullName ?? type.Name);

    /// <summary>
    /// Creates a new message with the specified correlation ID.
    /// </summary>
    public T WithCorrelationId<T>(string correlationId) where T : MessageBase
    {
        var clone = (T)MemberwiseClone();
        typeof(MessageBase).GetProperty(nameof(CorrelationId))!.SetValue(clone, correlationId);
        return clone;
    }
}

