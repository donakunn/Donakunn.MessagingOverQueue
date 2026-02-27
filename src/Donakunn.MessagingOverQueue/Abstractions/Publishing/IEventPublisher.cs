using Donakunn.MessagingOverQueue.Abstractions.Messages;

namespace Donakunn.MessagingOverQueue.Abstractions.Publishing;

/// <summary>
/// Specialized interface for publishing events.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes an event to subscribers.
    /// </summary>
    /// <typeparam name="T">The type of event.</typeparam>
    /// <param name="event">The event to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent;

    /// <summary>
    /// Schedules an event for delivery after the specified delay.
    /// Requires outbox persistence to be configured.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when outbox persistence is not configured.</exception>
    Task PublishAsync<T>(T @event, TimeSpan delay, CancellationToken cancellationToken = default)
        where T : IEvent
        => throw new NotSupportedException(
            "Delayed publishing requires outbox persistence. Call UsePersistence().WithOutbox() during setup.");
}

/// <summary>
/// Specialized interface for sending commands.
/// </summary>
public interface ICommandSender
{
    /// <summary>
    /// Sends a command to a handler.
    /// </summary>
    /// <typeparam name="T">The type of command.</typeparam>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendAsync<T>(T command, CancellationToken cancellationToken = default) where T : ICommand;

    /// <summary>
    /// Sends a command to a specific queue.
    /// </summary>
    /// <typeparam name="T">The type of command.</typeparam>
    /// <param name="command">The command to send.</param>
    /// <param name="queueName">The target queue name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendAsync<T>(T command, string queueName, CancellationToken cancellationToken = default) where T : ICommand;

    /// <summary>
    /// Schedules a command for delivery after the specified delay.
    /// Requires outbox persistence to be configured.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when outbox persistence is not configured.</exception>
    Task SendAsync<T>(T command, TimeSpan delay, CancellationToken cancellationToken = default)
        where T : ICommand
        => throw new NotSupportedException(
            "Delayed sending requires outbox persistence. Call UsePersistence().WithOutbox() during setup.");
}

