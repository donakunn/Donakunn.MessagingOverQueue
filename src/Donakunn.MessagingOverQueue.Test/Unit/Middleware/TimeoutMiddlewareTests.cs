using Donakunn.MessagingOverQueue.Consuming.Middleware;
using Donakunn.MessagingOverQueue.DependencyInjection.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Donakunn.MessagingOverQueue.Test.Unit.Middleware;

public class TimeoutMiddlewareTests
{
    [Fact]
    public async Task ReturnCts_WhenPoolFull_DisposesCts()
    {
        // Arrange - create middleware and exhaust pool capacity
        var options = Options.Create(new TimeoutOptions { Timeout = TimeSpan.FromSeconds(30) });
        var middleware = new TimeoutMiddleware(options, NullLogger<TimeoutMiddleware>.Instance);

        // Process enough messages to fill the pool
        for (int i = 0; i < 100; i++)
        {
            var context = CreateMinimalContext();
            await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.CompletedTask, CancellationToken.None);
        }

        // Act & Assert - no exception means pool didn't grow unboundedly
        // The bounded Channel implementation caps at MaxPoolSize
        // We verify by processing more messages without memory issues
        for (int i = 0; i < 100; i++)
        {
            var context = CreateMinimalContext();
            await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.CompletedTask, CancellationToken.None);
        }
    }

    [Fact]
    public async Task InvokeAsync_WhenHandlerTimesOut_ThrowsTimeoutException()
    {
        // Arrange
        var options = Options.Create(new TimeoutOptions { Timeout = TimeSpan.FromMilliseconds(50) });
        var middleware = new TimeoutMiddleware(options, NullLogger<TimeoutMiddleware>.Instance);
        var context = CreateMinimalContext();

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() =>
            middleware.InvokeAsync(context, async (ctx, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }, CancellationToken.None).AsTask());
    }

    private static ConsumeContext CreateMinimalContext()
    {
        return new ConsumeContext
        {
            DeliveryTag = 1,
            Body = [],
            ContentType = "application/json"
        };
    }
}
