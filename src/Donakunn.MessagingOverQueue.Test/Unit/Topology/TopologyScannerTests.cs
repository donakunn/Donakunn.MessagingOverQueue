using Donakunn.MessagingOverQueue.Abstractions.Consuming;
using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Topology;
using Donakunn.MessagingOverQueue.Topology.Abstractions;
using Donakunn.MessagingOverQueue.Topology.Attributes;
using System.Reflection;
using Xunit;

namespace Donakunn.MessagingOverQueue.Test.Unit.Topology;

public class TopologyScannerTests
{
    private readonly TopologyScanner _scanner = new();

    [Fact]
    public void ScanForMessageTypes_FindsEventTypes()
    {
        var messageTypes = _scanner.ScanForMessageTypes(typeof(TestEvent).Assembly);
        Assert.Contains(messageTypes, m => m.MessageType == typeof(TestEvent));
    }

    [Fact]
    public void ScanForMessageTypes_FindsCommandTypes()
    {
        var messageTypes = _scanner.ScanForMessageTypes(typeof(TestCommand).Assembly);
        var commandType = messageTypes.FirstOrDefault(m => m.MessageType == typeof(TestCommand));
        Assert.NotNull(commandType);
        Assert.True(commandType.IsCommand);
        Assert.False(commandType.IsEvent);
    }

    [Fact]
    public void ScanForMessageTypes_CorrectlyIdentifiesEventVsCommand()
    {
        var messageTypes = _scanner.ScanForMessageTypes(typeof(TestEvent).Assembly);
        var eventType = messageTypes.FirstOrDefault(m => m.MessageType == typeof(TestEvent));
        var commandType = messageTypes.FirstOrDefault(m => m.MessageType == typeof(TestCommand));
        Assert.NotNull(eventType);
        Assert.NotNull(commandType);
        Assert.True(eventType.IsEvent);
        Assert.False(eventType.IsCommand);
        Assert.True(commandType.IsCommand);
        Assert.False(commandType.IsEvent);
    }

    [Fact]
    public void ScanForMessageTypes_RespectsAutoDiscoverAttribute()
    {
        var messageTypes = _scanner.ScanForMessageTypes(typeof(NonDiscoverableMessage).Assembly);
        Assert.DoesNotContain(messageTypes, m => m.MessageType == typeof(NonDiscoverableMessage));
    }

    [Fact]
    public void ScanForMessageTypes_NullAssemblies_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _scanner.ScanForMessageTypes(null!));
    }

    [Fact]
    public void ScanForMessageTypes_EmptyAssemblies_ReturnsEmpty()
    {
        var messageTypes = _scanner.ScanForMessageTypes();
        Assert.Empty(messageTypes);
    }

    [Fact]
    public void ScanForHandlers_FindsHandlerTypes()
    {
        var handlers = _scanner.ScanForHandlers(typeof(TestEventHandler).Assembly);
        Assert.Contains(handlers, h => h.HandlerType == typeof(TestEventHandler));
    }

    [Fact]
    public void ScanForHandlers_IdentifiesMessageType()
    {
        var handlers = _scanner.ScanForHandlers(typeof(TestEventHandler).Assembly);
        var handler = handlers.FirstOrDefault(h => h.HandlerType == typeof(TestEventHandler));
        Assert.NotNull(handler);
        Assert.Equal(typeof(TestEvent), handler.MessageType);
    }

    [Fact]
    public void ScanForHandlers_IgnoresAbstractHandlers()
    {
        var handlers = _scanner.ScanForHandlers(typeof(AbstractHandler).Assembly);
        Assert.DoesNotContain(handlers, h => h.HandlerType == typeof(AbstractHandler));
    }

    [Fact]
    public void ScanForHandlerTopology_ReturnsCompleteInfo()
    {
        var topologyInfos = _scanner.ScanForHandlerTopology(typeof(TestEventHandler).Assembly);
        var info = topologyInfos.FirstOrDefault(t => t.HandlerType == typeof(TestEventHandler));
        Assert.NotNull(info);
        Assert.Equal(typeof(TestEvent), info.MessageType);
        Assert.True(info.IsEvent);
        Assert.False(info.IsCommand);
    }

    [Fact]
    public void ScanForHandlerTopology_IncludesConsumerQueueConfig()
    {
        var topologyInfos = _scanner.ScanForHandlerTopology(typeof(HandlerWithConsumerQueue).Assembly);
        var info = topologyInfos.FirstOrDefault(t => t.HandlerType == typeof(HandlerWithConsumerQueue));
        Assert.NotNull(info);
        Assert.NotNull(info.ConsumerQueueConfig);
        Assert.Equal(50, info.ConsumerQueueConfig.PrefetchCount);
    }

    [Fact]
    public void ScanForHandlerTopology_ExcludesMessageWithAutoDiscoverFalse()
    {
        var topologyInfos = _scanner.ScanForHandlerTopology(typeof(NonDiscoverableMessageHandler).Assembly);
        Assert.DoesNotContain(topologyInfos, t => t.MessageType == typeof(NonDiscoverableMessage));
    }

    [Fact]
    public async Task ScanForHandlers_IsThreadSafe()
    {
        const int iterations = 50;
        var results = new System.Collections.Concurrent.ConcurrentBag<IReadOnlyCollection<HandlerTypeInfo>>();

        var tasks = Enumerable.Range(0, iterations)
            .Select(_ => Task.Run(() =>
            {
                var handlers = _scanner.ScanForHandlers(typeof(TestEventHandler).Assembly);
                results.Add(handlers);
            }));

        await Task.WhenAll(tasks);

        Assert.Equal(iterations, results.Count);
        var firstCount = results.First().Count;
        Assert.All(results, r => Assert.Equal(firstCount, r.Count));
    }

    #region Test Types

    public class TestEvent : IEvent
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; } = null;
        public string? CausationId { get; } = null;
        public string MessageType { get; } = nameof(TestEvent);
    }

    public class TestCommand : ICommand
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; } = null;
        public string? CausationId { get; } = null;
        public string MessageType { get; } = nameof(TestCommand);
    }

    [Message(AutoDiscover = false)]
    public class NonDiscoverableMessage : IEvent
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; } = null;
        public string? CausationId { get; } = null;
        public string MessageType { get; } = nameof(NonDiscoverableMessage);
    }

    public class TestEventHandler : IMessageHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent message, IMessageContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [ConsumerQueue(PrefetchCount = 50)]
    public class HandlerWithConsumerQueue : IMessageHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent message, IMessageContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public class NonDiscoverableMessageHandler : IMessageHandler<NonDiscoverableMessage>
    {
        public Task HandleAsync(NonDiscoverableMessage message, IMessageContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public abstract class AbstractHandler : IMessageHandler<TestEvent>
    {
        public abstract Task HandleAsync(TestEvent message, IMessageContext context, CancellationToken cancellationToken);
    }

    #endregion
}
