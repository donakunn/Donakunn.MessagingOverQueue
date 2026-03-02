using System.Diagnostics.Metrics;

namespace Donakunn.MessagingOverQueue.Diagnostics;

/// <summary>
/// Central metrics for the messaging library using System.Diagnostics.Metrics.
/// Consumers of this library can listen via OpenTelemetry, Prometheus exporter, etc.
/// </summary>
public static class MessagingMetrics
{
    public static readonly Meter Meter = new("Donakunn.MessagingOverQueue", "1.0.0");

    // Consumer metrics
    public static readonly UpDownCounter<int> ActiveTasks = Meter.CreateUpDownCounter<int>(
        "messaging.consumer.active_tasks", description: "Number of active message processing tasks");

    public static readonly Counter<long> MessagesConsumed = Meter.CreateCounter<long>(
        "messaging.consumer.messages_consumed", description: "Total messages consumed");

    public static readonly Counter<long> MessagesFailed = Meter.CreateCounter<long>(
        "messaging.consumer.messages_failed", description: "Total messages that failed processing");

    // Cache metrics
    public static readonly UpDownCounter<int> InFlightCacheSize = Meter.CreateUpDownCounter<int>(
        "messaging.consumer.inflight_cache_size",
        description: "Current size of in-flight entries cache");

    public static readonly UpDownCounter<int> RecentlyAckedCacheSize = Meter.CreateUpDownCounter<int>(
        "messaging.consumer.recently_acked_cache_size",
        description: "Current size of recently acked cache");

    // Outbox metrics
    public static readonly Histogram<double> OutboxBatchDuration = Meter.CreateHistogram<double>(
        "messaging.outbox.batch_duration_ms", description: "Outbox batch processing duration in ms");

    public static readonly Counter<long> OutboxMessagesPublished = Meter.CreateCounter<long>(
        "messaging.outbox.messages_published", description: "Total outbox messages published");

    public static readonly Counter<long> OutboxMessagesFailed = Meter.CreateCounter<long>(
        "messaging.outbox.messages_failed", description: "Total outbox messages failed");

    // Circuit breaker metrics
    public static readonly Counter<int> CircuitBreakerStateTransitions = Meter.CreateCounter<int>(
        "messaging.circuit_breaker.state_transitions", description: "Circuit breaker state transitions");
}
