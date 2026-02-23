using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Abstractions.Publishing;
using Donakunn.MessagingOverQueue.Publishing.Middleware;
using Donakunn.MessagingOverQueue.Topology;

namespace Donakunn.MessagingOverQueue.RedisStreams;

/// <summary>
/// Redis Streams implementation of the message publisher interfaces.
/// Wraps RedisStreamsPublisher with middleware pipeline support.
/// </summary>
internal sealed class RedisStreamsMessagePublisher : IMessagePublisher, IEventPublisher, ICommandSender
{
    private readonly RedisStreamsPublisher _publisher;
    private readonly IMessageRoutingResolver _routingResolver;
    private readonly Func<PublishContext, CancellationToken, Task> _pipeline;

    public RedisStreamsMessagePublisher(
        RedisStreamsPublisher publisher,
        IEnumerable<IPublishMiddleware> middlewares,
        IMessageRoutingResolver routingResolver)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        ArgumentNullException.ThrowIfNull(middlewares);
        _routingResolver = routingResolver ?? throw new ArgumentNullException(nameof(routingResolver));

        // Build pipeline once — all publish middlewares are singletons
        _pipeline = PublishPipeline.Build(middlewares, _publisher.PublishAsync);
    }

    /// <inheritdoc />
    public Task PublishAsync<T>(T message, string? exchangeName = null, string? routingKey = null, CancellationToken cancellationToken = default) where T : IMessage
    {
        return PublishAsync(message, new PublishOptions
        {
            ExchangeName = exchangeName,
            RoutingKey = routingKey
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task PublishAsync<T>(T message, PublishOptions options, CancellationToken cancellationToken = default) where T : IMessage
    {
        // Single cached lookup instead of 3 separate topology lookups
        var routing = _routingResolver.ResolveRouting<T>();
        var exchangeName = options.ExchangeName ?? routing.ExchangeName;
        var routingKey = options.RoutingKey ?? routing.RoutingKey;
        var queueName = routing.QueueName;

        var context = new PublishContext
        {
            Message = message,
            MessageType = typeof(T),
            ExchangeName = exchangeName,
            RoutingKey = routingKey,
            QueueName = queueName,
            Persistent = options.Persistent,
            Priority = options.Priority,
            TimeToLive = options.TimeToLive,
            WaitForConfirm = options.WaitForConfirm,
            ConfirmTimeout = options.ConfirmTimeout
        };

        if (options.Headers != null)
        {
            foreach (var header in options.Headers)
            {
                context.Headers[header.Key] = header.Value;
            }
        }

        await _pipeline(context, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent
    {
        var routing = _routingResolver.ResolveRouting<T>();
        return PublishAsync(@event, routing.ExchangeName, routing.RoutingKey, cancellationToken);
    }

    /// <inheritdoc />
    public Task SendAsync<T>(T command, CancellationToken cancellationToken = default) where T : ICommand
    {
        var routing = _routingResolver.ResolveRouting<T>();
        return SendAsync(command, routing.QueueName, cancellationToken);
    }

    /// <inheritdoc />
    public Task SendAsync<T>(T command, string queueName, CancellationToken cancellationToken = default) where T : ICommand
    {
        // Commands are sent directly to a specific stream (using queue name as routing key)
        return PublishAsync(command, string.Empty, queueName, cancellationToken);
    }
}
