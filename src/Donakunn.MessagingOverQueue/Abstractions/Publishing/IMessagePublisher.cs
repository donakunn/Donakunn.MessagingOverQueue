using Donakunn.MessagingOverQueue.Abstractions.Messages;

namespace Donakunn.MessagingOverQueue.Abstractions.Publishing;

/// <summary>
/// Interface for publishing messages to the message broker.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Publishes a message using auto-resolved routing from topology.
    /// </summary>
    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Publishes a message with explicit routing options.
    /// </summary>
    Task PublishAsync<T>(T message, PublishOptions options, CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Schedules a message for delivery after the specified delay.
    /// Requires outbox persistence to be configured.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when outbox persistence is not configured.</exception>
    Task PublishAsync<T>(T message, TimeSpan delay, CancellationToken cancellationToken = default)
        where T : IMessage
        => throw new NotSupportedException(
            "Delayed publishing requires outbox persistence. Call UsePersistence().WithOutbox() during setup.");
}

/// <summary>
/// Options for publishing a message.
/// </summary>
public class PublishOptions
{
    public string? ExchangeName { get; set; }
    public string? RoutingKey { get; set; }
    public bool Persistent { get; set; } = true;
    public byte? Priority { get; set; }
    public int? TimeToLive { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public bool WaitForConfirm { get; set; } = true;
    public TimeSpan ConfirmTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
