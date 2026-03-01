namespace Donakunn.MessagingOverQueue.Topology.Attributes;

/// <summary>
/// Specifies the consumer queue configuration for a message handler.
/// When applied to a handler class, this attribute allows customizing the queue
/// that the handler consumes from, separate from the message's default topology.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ConsumerQueueAttribute : Attribute
{
    /// <summary>
    /// The queue type (classic, quorum, stream).
    /// </summary>
    public QueueType QueueType { get; init; } = QueueType.Classic;

    /// <summary>
    /// The prefetch count for this consumer. If -1, uses default.
    /// </summary>
    public ushort PrefetchCount { get; init; } = 10;

    /// <summary>
    /// The maximum concurrency for this consumer. If -1, uses default.
    /// </summary>
    public int MaxConcurrency { get; init; } = 1;
}

/// <summary>
/// Queue types.
/// </summary>
public enum QueueType
{
    /// <summary>
    /// Classic queue - standard queue.
    /// </summary>
    Classic,

    /// <summary>
    /// Quorum queue - highly available, replicated queue.
    /// </summary>
    Quorum,

    /// <summary>
    /// Stream queue - high-throughput, append-only log.
    /// </summary>
    Stream,

    /// <summary>
    /// Lazy queue - stores messages to disk as soon as possible.
    /// </summary>
    Lazy
}
