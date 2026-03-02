namespace Donakunn.MessagingOverQueue.Persistence;

/// <summary>
/// In-process signal that wakes outbox processor workers when a message is written to the outbox.
/// </summary>
public interface IOutboxSignal
{
    /// <summary>
    /// Signals all registered processor handles. Called by <see cref="OutboxPublisher"/> after each write.
    /// </summary>
    void Signal();

    /// <summary>
    /// Registers a processor and returns its dedicated wait handle. Dispose the handle on processor shutdown.
    /// </summary>
    IOutboxSignalHandle Register();
}

/// <summary>
/// Per-processor wait handle returned by <see cref="IOutboxSignal.Register"/>.
/// Disposing unregisters this processor from the fan-out list.
/// </summary>
public interface IOutboxSignalHandle : IDisposable
{
    /// <summary>
    /// Waits until signaled or <paramref name="timeout"/> elapses, then returns.
    /// Both outcomes (signal and timeout) should trigger a batch processing cycle.
    /// Throws <see cref="OperationCanceledException"/> if <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
