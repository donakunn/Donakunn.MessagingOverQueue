namespace Donakunn.MessagingOverQueue.Publishing.Middleware;

/// <summary>
/// Builds and executes the publish middleware pipeline.
/// </summary>
public class PublishPipeline
{
    private readonly Func<PublishContext, CancellationToken, Task> _pipeline;

    public PublishPipeline(IEnumerable<IPublishMiddleware> middlewares, Func<PublishContext, CancellationToken, Task> terminalHandler)
    {
        var valuePipeline = BuildPipeline(middlewares, (ctx, ct) => new ValueTask(terminalHandler(ctx, ct)));
        _pipeline = (ctx, ct) => valuePipeline(ctx, ct).AsTask();
    }

    /// <summary>
    /// Executes the pipeline for the given context.
    /// </summary>
    public Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        return _pipeline(context, cancellationToken);
    }

    /// <summary>
    /// Builds a reusable pipeline delegate from the given middlewares and terminal handler.
    /// Call once at startup and reuse the returned delegate for all publishes.
    /// </summary>
    public static Func<PublishContext, CancellationToken, Task> Build(
        IEnumerable<IPublishMiddleware> middlewares,
        Func<PublishContext, CancellationToken, Task> terminalHandler)
    {
        var valuePipeline = BuildPipeline(middlewares, (ctx, ct) => new ValueTask(terminalHandler(ctx, ct)));
        return (ctx, ct) => valuePipeline(ctx, ct).AsTask();
    }

    private static Func<PublishContext, CancellationToken, ValueTask> BuildPipeline(
        IEnumerable<IPublishMiddleware> middlewares,
        Func<PublishContext, CancellationToken, ValueTask> terminalHandler)
    {
        Func<PublishContext, CancellationToken, ValueTask> current = terminalHandler;

        foreach (var middleware in middlewares.Reverse())
        {
            var next = current;
            var currentMiddleware = middleware;
            current = (ctx, ct) => currentMiddleware.InvokeAsync(ctx, next, ct);
        }

        return current;
    }
}
