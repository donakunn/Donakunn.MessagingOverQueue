using Donakunn.MessagingOverQueue.Resilience.CircuitBreaker;
using Polly.CircuitBreaker;

namespace Donakunn.MessagingOverQueue.Test.Unit.Resilience;

/// <summary>
/// Unit tests for PollyCircuitBreaker.
/// </summary>
public class CircuitBreakerTests
{
    [Fact]
    public async Task ExecuteAsync_SuccessfulOperation_Completes()
    {
        // Arrange
        var options = CreateTestOptions();
        using var circuitBreaker = new PollyCircuitBreaker(options);

        // Act
        var executed = false;
        await circuitBreaker.ExecuteAsync(ct =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        // Assert
        Assert.True(executed);
        Assert.Equal(CircuitState.Closed, circuitBreaker.State);
    }

    [Fact]
    public async Task ExecuteAsync_WithReturn_ReturnsValue()
    {
        // Arrange
        var options = CreateTestOptions();
        using var circuitBreaker = new PollyCircuitBreaker(options);

        // Act
        var result = await circuitBreaker.ExecuteAsync(ct => Task.FromResult(42));

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteAsync_ExceedsFailureThreshold_OpensCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureRateThreshold = 0.5,
            MinimumThroughput = 2,
            SamplingDuration = TimeSpan.FromSeconds(30),
            DurationOfBreak = TimeSpan.FromMinutes(1)
        };
        using var circuitBreaker = new PollyCircuitBreaker(options);

        // Act - Force failures to trip the circuit
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync<int>(ct =>
                    throw new InvalidOperationException("Forced failure"));
            }
            catch (InvalidOperationException) { }
            catch (CircuitBreakerOpenException) { break; }
        }

        // Assert - After enough failures, circuit should open
        var state = circuitBreaker.State;
        Assert.True(state == CircuitState.Open || state == CircuitState.HalfOpen,
            $"Expected Open or HalfOpen but got {state}");
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var options = CreateTestOptions();
        var circuitBreaker = new PollyCircuitBreaker(options);

        // Act & Assert
        circuitBreaker.Dispose();
        circuitBreaker.Dispose(); // Should not throw
    }

    [Fact]
    public async Task Dispose_ConcurrentCalls_DoesNotDeadlock()
    {
        // Arrange
        var options = CreateTestOptions();
        var circuitBreaker = new PollyCircuitBreaker(options);

        // Act - concurrent disposal should not deadlock
        var task1 = Task.Run(() => circuitBreaker.Dispose());
        var task2 = Task.Run(() => circuitBreaker.Dispose());

        await Task.WhenAll(task1, task2).WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - if we reach here without timeout, no deadlock occurred
    }

    [Fact]
    public async Task ExecuteAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var options = CreateTestOptions();
        var circuitBreaker = new PollyCircuitBreaker(options);
        circuitBreaker.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await circuitBreaker.ExecuteAsync(ct => Task.CompletedTask));
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentCalls_AreHandledCorrectly()
    {
        // Arrange
        var options = CreateTestOptions();
        using var circuitBreaker = new PollyCircuitBreaker(options);
        const int callCount = 100;
        var counter = 0;

        // Act
        var tasks = Enumerable.Range(0, callCount)
            .Select(_ => circuitBreaker.ExecuteAsync(ct =>
            {
                Interlocked.Increment(ref counter);
                return Task.CompletedTask;
            }));

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(callCount, counter);
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PollyCircuitBreaker(null!));
    }

    [Fact]
    public async Task ExecuteAsync_PassesCancellationToken()
    {
        // Arrange
        var options = CreateTestOptions();
        using var circuitBreaker = new PollyCircuitBreaker(options);
        using var cts = new CancellationTokenSource();
        CancellationToken? capturedToken = null;

        // Act
        await circuitBreaker.ExecuteAsync(ct =>
        {
            capturedToken = ct;
            return Task.CompletedTask;
        }, cts.Token);

        // Assert
        Assert.NotNull(capturedToken);
    }

    private static CircuitBreakerOptions CreateTestOptions()
    {
        return new CircuitBreakerOptions
        {
            FailureRateThreshold = 0.5,
            MinimumThroughput = 10,
            SamplingDuration = TimeSpan.FromSeconds(30),
            DurationOfBreak = TimeSpan.FromSeconds(30)
        };
    }
}
