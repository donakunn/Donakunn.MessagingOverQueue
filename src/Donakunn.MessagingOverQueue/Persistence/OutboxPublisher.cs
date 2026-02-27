using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Abstractions.Publishing;
using Donakunn.MessagingOverQueue.Abstractions.Serialization;
using Donakunn.MessagingOverQueue.Persistence.Entities;
using Donakunn.MessagingOverQueue.Persistence.Repositories;
using Donakunn.MessagingOverQueue.Topology;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Donakunn.MessagingOverQueue.Persistence;

/// <summary>
/// Publisher that stores messages in the outbox for reliable delivery.
/// Use this when you need transactional consistency with your database operations.
/// </summary>
public sealed class OutboxPublisher(
    IOutboxRepository repository,
    IMessageSerializer serializer,
    IMessageRoutingResolver routingResolver,
    ILogger<OutboxPublisher> logger) : IMessagePublisher, IEventPublisher, ICommandSender
{
    private readonly IOutboxRepository _repository = repository;
    private readonly IMessageSerializer _serializer = serializer;
    private readonly IMessageRoutingResolver _routingResolver = routingResolver;
    private readonly ILogger<OutboxPublisher> _logger = logger;

    public async Task PublishAsync<T>(T message, string? exchangeName = null, string? routingKey = null, CancellationToken cancellationToken = default) where T : IMessage
    {
        await PublishAsync(message, new PublishOptions
        {
            ExchangeName = exchangeName,
            RoutingKey = routingKey
        }, cancellationToken);
    }

    public async Task PublishAsync<T>(T message, PublishOptions options, CancellationToken cancellationToken = default) where T : IMessage
    {
        // Use routing resolver for defaults if not explicitly specified
        var routing = _routingResolver.ResolveRouting<T>();
        var exchangeName = options.ExchangeName ?? routing.ExchangeName;
        var routingKey = options.RoutingKey ?? routing.RoutingKey;
        var queueName = routing.StreamKey;

        var entry = MessageStoreEntry.CreateOutboxEntry(
            message.Id,
            message.MessageType,
            _serializer.Serialize(message),
            exchangeName,
            routingKey,
            queueName,
            options.Headers != null ? JsonSerializer.Serialize(options.Headers, Abstractions.Serialization.InternalJsonContext.Default.DictionaryStringString) : null,
            message.CorrelationId);

        if (!await _repository.TryAddAsync(entry, cancellationToken))
        {
            _logger.LogDebug("Skipped duplicate message {MessageId} already present in outbox", message.Id);
            return;
        }

        _logger.LogDebug("Added message {MessageId} to outbox for exchange '{Exchange}' with routing key '{RoutingKey}' and queue '{QueueName}'",
            message.Id, exchangeName, routingKey, queueName);
    }

    public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent
    {
        var routing = _routingResolver.ResolveRouting<T>();
        return PublishAsync(@event, routing.ExchangeName, routing.RoutingKey, cancellationToken);
    }

    public Task PublishAsync<T>(T @event, TimeSpan delay, CancellationToken cancellationToken = default) where T : IEvent
    {
        var routing = _routingResolver.ResolveRouting<T>();
        return PublishDelayedAsync(@event, routing.ExchangeName, routing.RoutingKey, delay, cancellationToken);
    }

    public Task SendAsync<T>(T command, CancellationToken cancellationToken = default) where T : ICommand
    {
        var routing = _routingResolver.ResolveRouting<T>();
        return SendAsync(command, routing.StreamKey, cancellationToken);
    }

    public Task SendAsync<T>(T command, string queueName, CancellationToken cancellationToken = default) where T : ICommand
    {
        return PublishAsync(command, string.Empty, queueName, cancellationToken);
    }

    public Task SendAsync<T>(T command, TimeSpan delay, CancellationToken cancellationToken = default) where T : ICommand
    {
        var routing = _routingResolver.ResolveRouting<T>();
        return PublishDelayedAsync(command, string.Empty, routing.StreamKey, delay, cancellationToken);
    }

    private async Task PublishDelayedAsync<T>(
        T message,
        string? exchangeName,
        string? routingKey,
        TimeSpan delay,
        CancellationToken cancellationToken)
        where T : IMessage
    {
        var routing = _routingResolver.ResolveRouting<T>();
        var queueName = routing.StreamKey;
        var scheduledAt = DateTime.UtcNow.Add(delay);

        var entry = MessageStoreEntry.CreateOutboxEntry(
            message.Id,
            message.MessageType,
            _serializer.Serialize(message),
            exchangeName,
            routingKey,
            queueName,
            headers: null,
            correlationId: message.CorrelationId,
            scheduledAt: scheduledAt);

        if (!await _repository.TryAddAsync(entry, cancellationToken))
        {
            _logger.LogDebug("Skipped duplicate message {MessageId} already present in outbox", message.Id);
            return;
        }

        _logger.LogDebug(
            "Scheduled message {MessageId} for delivery at {ScheduledAt} (delay: {Delay})",
            message.Id, scheduledAt, delay);
    }
}

