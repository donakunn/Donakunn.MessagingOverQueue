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
    public void ScanForMessageTypes_FindsMessageTypes()
    {
        var messageTypes = _scanner.ScanForMessageTypes(typeof(TestMessage).Assembly);
        Assert.Contains(messageTypes, m => m.MessageType == typeof(TestMessage));
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
        var handlers = _scanner.ScanForHandlers(typeof(TestMessageHandler).Assembly);
        Assert.Contains(handlers, h => h.HandlerType == typeof(TestMessageHandler));
    }

    [Fact]
    public void ScanForHandlers_IdentifiesMessageType()
    {
        var handlers = _scanner.ScanForHandlers(typeof(TestMessageHandler).Assembly);
        var handler = handlers.FirstOrDefault(h => h.HandlerType == typeof(TestMessageHandler));
        Assert.NotNull(handler);
        Assert.Equal(typeof(TestMessage), handler.MessageType);
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
        var topologyInfos = _scanner.ScanForHandlerTopology(typeof(TestMessageHandler).Assembly);
        var info = topologyInfos.FirstOrDefault(t => t.HandlerType == typeof(TestMessageHandler));
        Assert.NotNull(info);
        Assert.Equal(typeof(TestMessage), info.MessageType);
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
                var handlers = _scanner.ScanForHandlers(typeof(TestMessageHandler).Assembly);
                results.Add(handlers);
            }));

        await Task.WhenAll(tasks);

        Assert.Equal(iterations, results.Count);
        var firstCount = results.First().Count;
        Assert.All(results, r => Assert.Equal(firstCount, r.Count));
    }

    #region Test Types

    public record TestMessage : MessageBase;

    [Message(AutoDiscover = false)]
    public record NonDiscoverableMessage : MessageBase;

    public class TestMessageHandler : IMessageHandler<TestMessage>
    {
        public Task HandleAsync(TestMessage message, IMessageContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [ConsumerQueue(PrefetchCount = 50)]
    public class HandlerWithConsumerQueue : IMessageHandler<TestMessage>
    {
        public Task HandleAsync(TestMessage message, IMessageContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public class NonDiscoverableMessageHandler : IMessageHandler<NonDiscoverableMessage>
    {
        public Task HandleAsync(NonDiscoverableMessage message, IMessageContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public abstract class AbstractHandler : IMessageHandler<TestMessage>
    {
        public abstract Task HandleAsync(TestMessage message, IMessageContext context, CancellationToken cancellationToken);
    }

    #endregion
}
