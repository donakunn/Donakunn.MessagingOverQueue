using System.Collections.Concurrent;

namespace Donakunn.MessagingOverQueue.Persistence;

/// <summary>
/// In-process fan-out signal. Singleton. Wakes every registered <see cref="OutboxProcessor"/>
/// worker when a message is written to the outbox.
/// </summary>
internal sealed class OutboxSignal : IOutboxSignal
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _semaphores = new();
    private int _nextId;

    /// <inheritdoc />
    public void Signal()
    {
        foreach (var semaphore in _semaphores.Values)
        {
            try
            {
                semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // Processor already has a pending wake-up queued — intentional no-op.
            }
            catch (ObjectDisposedException)
            {
                // Handle was disposed between iteration snapshot and Release() call — benign race.
            }
        }
    }

    /// <inheritdoc />
    public IOutboxSignalHandle Register()
    {
        var id = Interlocked.Increment(ref _nextId);
        var semaphore = new SemaphoreSlim(0, 1);
        _semaphores[id] = semaphore;
        return new Handle(semaphore, () => _semaphores.TryRemove(id, out _));
    }

    private sealed class Handle : IOutboxSignalHandle
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly Action _unregister;
        private int _disposed;

        public Handle(SemaphoreSlim semaphore, Action unregister)
        {
            _semaphore = semaphore;
            _unregister = unregister;
        }

        /// <inheritdoc />
        public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            // Return value (bool) is intentionally discarded:
            // both "signaled" and "timed out" are valid reasons to run a processing cycle.
            await _semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _unregister();
                _semaphore.Dispose();
            }
        }
    }
}
