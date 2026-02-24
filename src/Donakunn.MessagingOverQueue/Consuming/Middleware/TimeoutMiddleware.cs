using System.Collections.Concurrent;
using Donakunn.MessagingOverQueue.DependencyInjection.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Donakunn.MessagingOverQueue.Consuming.Middleware;

/// <summary>
/// Middleware that enforces a maximum processing time for messages.
/// If processing exceeds the timeout, the operation is cancelled.
/// Pools CancellationTokenSource instances to reduce GC pressure on the hot path.
/// </summary>
public class TimeoutMiddleware : IOrderedConsumeMiddleware
{
    private readonly TimeoutOptions _options;
    private readonly ILogger<TimeoutMiddleware> _logger;
    private readonly ConcurrentBag<CancellationTokenSource> _ctsPool = new();
    private const int MaxPoolSize = 64;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeoutMiddleware"/> class.
    /// </summary>
    /// <param name="options">The timeout options.</param>
    /// <param name="logger">The logger.</param>
    public TimeoutMiddleware(
        IOptions<TimeoutOptions> options,
        ILogger<TimeoutMiddleware> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public int Order => MiddlewareOrder.Timeout;

    /// <inheritdoc />
    public async ValueTask InvokeAsync(
        ConsumeContext context,
        Func<ConsumeContext, CancellationToken, ValueTask> next,
        CancellationToken cancellationToken)
    {
        var cts = RentCts();
        var registration = cancellationToken.Register(static state => ((CancellationTokenSource)state!).Cancel(), cts);

        try
        {
            cts.CancelAfter(_options.Timeout);
            await next(context, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Timeout occurred (not external cancellation)
            var timeoutException = new TimeoutException(
                $"Message processing timed out after {_options.Timeout.TotalSeconds:F1} seconds");

            _logger.LogError(
                timeoutException,
                "Message processing timed out after {TimeoutSeconds}s, delivery tag: {DeliveryTag}",
                _options.Timeout.TotalSeconds,
                context.DeliveryTag);

            context.Exception = timeoutException;
            context.ShouldReject = true;
            context.RequeueOnReject = false; // Don't requeue timed-out messages by default

            throw timeoutException;
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
            ReturnCts(cts);
        }
    }

    private CancellationTokenSource RentCts()
    {
        return _ctsPool.TryTake(out var cts) ? cts : new CancellationTokenSource();
    }

    private void ReturnCts(CancellationTokenSource cts)
    {
        if (cts.TryReset() && _ctsPool.Count < MaxPoolSize)
        {
            _ctsPool.Add(cts);
        }
        else
        {
            cts.Dispose();
        }
    }
}
