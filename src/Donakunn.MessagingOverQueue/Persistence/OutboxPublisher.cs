using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Abstractions.Publishing;
using Donakunn.MessagingOverQueue.Abstractions.Serialization;
using Donakunn.MessagingOverQueue.Configuration.Options;
using Donakunn.MessagingOverQueue.Persistence.Entities;
using Donakunn.MessagingOverQueue.Persistence.Repositories;
using Donakunn.MessagingOverQueue.Topology;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Donakunn.MessagingOverQueue.Persistence;

/// <summary>
/// Publisher that stores messages in the outbox for reliable delivery.
/// Use this when you need transactional consistency with your database operations.
/// </summary>
public sealed class OutboxPublisher : IMessagePublisher
{
    private readonly IOutboxRepository _repository;
    private readonly IMessageSerializer _serializer;
    private readonly IMessageRoutingResolver _routingResolver;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxPublisher> _logger;

    public OutboxPublisher(
        IOutboxRepository repository,
        IMessageSerializer serializer,
        IMessageRoutingResolver routingResolver,
        IOptions<OutboxOptions> options,
        ILogger<OutboxPublisher> logger)
    {
        _repository = repository;
        _serializer = serializer;
        _routingResolver = routingResolver;
        _options = options.Value;
        _logger = logger;
    }

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : IMessage
    {
        return PublishAsync(message, new PublishOptions(), cancellationToken);
    }

    public async Task PublishAsync<T>(T message, PublishOptions options, CancellationToken cancellationToken = default) where T : IMessage
    {
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

        await _repository.AddAsync(entry, cancellationToken);

        _logger.LogDebug("Added message {MessageId} to outbox for exchange '{Exchange}' with routing key '{RoutingKey}' and queue '{QueueName}'",
            message.Id, exchangeName, routingKey, queueName);
    }

    public async Task PublishAsync<T>(T message, TimeSpan delay, CancellationToken cancellationToken = default) where T : IMessage
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero, nameof(delay));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delay, _options.MaxDelay, nameof(delay));

        var routing = _routingResolver.ResolveRouting<T>();
        var queueName = routing.StreamKey;
        var scheduledAt = DateTime.UtcNow.Add(delay);

        var entry = MessageStoreEntry.CreateOutboxEntry(
            message.Id,
            message.MessageType,
            _serializer.Serialize(message),
            routing.ExchangeName,
            routing.RoutingKey,
            queueName,
            headers: null,
            correlationId: message.CorrelationId,
            scheduledAt: scheduledAt);

        await _repository.AddAsync(entry, cancellationToken);

        _logger.LogDebug(
            "Scheduled message {MessageId} for delivery at {ScheduledAt} (delay: {Delay})",
            message.Id, scheduledAt, delay);
    }
}
