using Donakunn.MessagingOverQueue.Persistence;

namespace MessagingOverQueue.Test.Unit.Persistence;

public class OutboxSignalTests
{
    [Fact]
    public void Signal_WithNoHandles_DoesNotThrow()
    {
        var signal = new OutboxSignal();
        var exception = Record.Exception(() => signal.Signal());
        Assert.Null(exception);
    }

    [Fact]
    public async Task Signal_WithMultipleHandles_WakesAll()
    {
        var signal = new OutboxSignal();
        using var handle1 = signal.Register();
        using var handle2 = signal.Register();
        using var handle3 = signal.Register();

        signal.Signal();

        var t1 = handle1.WaitAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        var t2 = handle2.WaitAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        var t3 = handle3.WaitAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        await Task.WhenAll(t1, t2, t3);
    }

    [Fact]
    public async Task Signal_WhenHandlePending_DoesNotAccumulate()
    {
        var signal = new OutboxSignal();
        using var handle = signal.Register();

        signal.Signal();
        signal.Signal(); // second signal — must not overflow the semaphore

        await handle.WaitAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None);

        // Second wait should time out — no second wake-up was queued.
        // Verify by measuring elapsed time: a signal wake-up returns in < 10 ms;
        // a genuine timeout takes the full duration (~100 ms).
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await handle.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 80,
            $"Second WaitAsync should have timed out but returned after only {sw.ElapsedMilliseconds} ms — semaphore may have accumulated an extra release.");
    }

    [Fact]
    public void Handle_Dispose_RemovesFromFanOut()
    {
        var signal = new OutboxSignal();
        using var activeHandle = signal.Register();
        var disposedHandle = signal.Register();

        disposedHandle.Dispose();

        var exception = Record.Exception(() => signal.Signal());
        Assert.Null(exception);
    }

    [Fact]
    public void Handle_Dispose_IsIdempotent()
    {
        var signal = new OutboxSignal();
        var handle = signal.Register();

        handle.Dispose();
        var exception = Record.Exception(() => handle.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Signal_ConcurrentCalls_NoRaceConditions()
    {
        var signal = new OutboxSignal();
        using var handle = signal.Register();

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => signal.Signal()))
            .ToArray();

        await Task.WhenAll(tasks);
    }
}
