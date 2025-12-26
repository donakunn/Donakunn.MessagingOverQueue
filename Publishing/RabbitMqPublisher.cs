using AsyncronousComunication.Abstractions.Messages;
using AsyncronousComunication.Abstractions.Publishing;
using AsyncronousComunication.Connection;
using AsyncronousComunication.Publishing.Middleware;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace AsyncronousComunication.Publishing;

/// <summary>
/// RabbitMQ implementation of message publisher.
/// </summary>
public class RabbitMqPublisher : IMessagePublisher, IEventPublisher, ICommandSender
{
    private readonly IRabbitMqConnectionPool _connectionPool;
    private readonly IEnumerable<IPublishMiddleware> _middlewares;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(
        IRabbitMqConnectionPool connectionPool,
        IEnumerable<IPublishMiddleware> middlewares,
        ILogger<RabbitMqPublisher> logger)
    {
        _connectionPool = connectionPool;
        _middlewares = middlewares;
        _logger = logger;
    }

    public Task PublishAsync<T>(T message, string? exchangeName = null, string? routingKey = null, CancellationToken cancellationToken = default) where T : IMessage
    {
        return PublishAsync(message, new PublishOptions
        {
            ExchangeName = exchangeName,
            RoutingKey = routingKey
        }, cancellationToken);
    }

    public async Task PublishAsync<T>(T message, PublishOptions options, CancellationToken cancellationToken = default) where T : IMessage
    {
        var context = new PublishContext
        {
            Message = message,
            MessageType = typeof(T),
            ExchangeName = options.ExchangeName,
            RoutingKey = options.RoutingKey ?? GetDefaultRoutingKey<T>(),
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

        var pipeline = new PublishPipeline(_middlewares, PublishToRabbitMqAsync);
        await pipeline.ExecuteAsync(context, cancellationToken);
    }

    public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent
    {
        var exchangeName = GetExchangeName<T>();
        var routingKey = GetDefaultRoutingKey<T>();
        return PublishAsync(@event, exchangeName, routingKey, cancellationToken);
    }

    public Task SendAsync<T>(T command, CancellationToken cancellationToken = default) where T : ICommand
    {
        var queueName = GetQueueName<T>();
        return SendAsync(command, queueName, cancellationToken);
    }

    public Task SendAsync<T>(T command, string queueName, CancellationToken cancellationToken = default) where T : ICommand
    {
        return PublishAsync(command, string.Empty, queueName, cancellationToken);
    }

    private async Task PublishToRabbitMqAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var channel = await _connectionPool.GetChannelAsync(cancellationToken);
        try
        {
            var properties = new BasicProperties
            {
                Persistent = context.Persistent,
                ContentType = context.ContentType,
                MessageId = context.Message.Id.ToString(),
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Headers = context.Headers.ToDictionary(x => x.Key, x => x.Value)
            };

            if (context.Message.CorrelationId != null)
                properties.CorrelationId = context.Message.CorrelationId;

            if (context.Priority.HasValue)
                properties.Priority = context.Priority.Value;

            if (context.TimeToLive.HasValue)
                properties.Expiration = context.TimeToLive.Value.ToString();

            await channel.BasicPublishAsync(
                exchange: context.ExchangeName ?? string.Empty,
                routingKey: context.RoutingKey ?? string.Empty,
                mandatory: false,
                basicProperties: properties,
                body: context.Body ?? Array.Empty<byte>(),
                cancellationToken: cancellationToken);

            _logger.LogDebug("Message {MessageId} published successfully", context.Message.Id);
        }
        finally
        {
            _connectionPool.ReturnChannel(channel);
        }
    }

    private static string GetExchangeName<T>() where T : IMessage
    {
        var type = typeof(T);
        return $"{type.Namespace}.{type.Name}".Replace(".", "-").ToLowerInvariant();
    }

    private static string GetQueueName<T>() where T : IMessage
    {
        var type = typeof(T);
        return $"{type.Namespace}.{type.Name}".Replace(".", "-").ToLowerInvariant();
    }

    private static string GetDefaultRoutingKey<T>() where T : IMessage
    {
        var type = typeof(T);
        return type.FullName?.Replace(".", ".") ?? type.Name;
    }
}

