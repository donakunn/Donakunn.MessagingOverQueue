using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Abstractions.Publishing;
using Donakunn.MessagingOverQueue.Abstractions.Serialization;
using Donakunn.MessagingOverQueue.Persistence;
using Donakunn.MessagingOverQueue.Persistence.Entities;
using Donakunn.MessagingOverQueue.Persistence.Repositories;
using Donakunn.MessagingOverQueue.Topology;
using Microsoft.Extensions.Logging;
using Moq;

namespace MessagingOverQueue.Test.Unit.Publishing;

public class DelayedPublishingTests
{
    private record TestEvent : Event { }
    private record TestCommand : Command { }

    [Fact]
    public async Task IEventPublisher_PublishWithDelay_ThrowsWhenNotOutbox()
    {
        // Arrange — a bare implementation that only handles the immediate overload
        IEventPublisher publisher = new NonOutboxPublisher();

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(
            () => publisher.PublishAsync(new TestEvent(), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task ICommandSender_SendWithDelay_ThrowsWhenNotOutbox()
    {
        ICommandSender sender = new NonOutboxPublisher();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => sender.SendAsync(new TestCommand(), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task OutboxPublisher_PublishWithDelay_SetsScheduledAt()
    {
        // Arrange
        var capturedEntry = (MessageStoreEntry?)null;
        var mockRepo = new Mock<IOutboxRepository>();
        mockRepo
            .Setup(r => r.AddAsync(It.IsAny<MessageStoreEntry>(), It.IsAny<CancellationToken>()))
            .Callback<MessageStoreEntry, CancellationToken>((e, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        var mockSerializer = new Mock<IMessageSerializer>();
        mockSerializer
            .Setup(s => s.Serialize(It.IsAny<IMessage>()))
            .Returns([]);

        var mockResolver = new Mock<IMessageRoutingResolver>();
        mockResolver
            .Setup(r => r.ResolveRouting<TestEvent>())
            .Returns(new RoutingInfo("test.test-event"));

        var mockLogger = new Mock<ILogger<OutboxPublisher>>();

        IEventPublisher publisher = new OutboxPublisher(
            mockRepo.Object, mockSerializer.Object, mockResolver.Object, mockLogger.Object);

        var before = DateTime.UtcNow;
        var delay = TimeSpan.FromMinutes(10);

        // Act
        await publisher.PublishAsync(new TestEvent(), delay);

        // Assert
        Assert.NotNull(capturedEntry);
        Assert.NotNull(capturedEntry!.ScheduledAt);
        Assert.True(capturedEntry.ScheduledAt >= before.Add(delay));
        Assert.True(capturedEntry.ScheduledAt <= DateTime.UtcNow.Add(delay).AddSeconds(1));
    }

    [Fact]
    public async Task OutboxPublisher_PublishWithoutDelay_LeavesScheduledAtNull()
    {
        var capturedEntry = (MessageStoreEntry?)null;
        var mockRepo = new Mock<IOutboxRepository>();
        mockRepo
            .Setup(r => r.AddAsync(It.IsAny<MessageStoreEntry>(), It.IsAny<CancellationToken>()))
            .Callback<MessageStoreEntry, CancellationToken>((e, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        var mockSerializer = new Mock<IMessageSerializer>();
        mockSerializer.Setup(s => s.Serialize(It.IsAny<IMessage>())).Returns([]);

        var mockResolver = new Mock<IMessageRoutingResolver>();
        mockResolver
            .Setup(r => r.ResolveRouting<TestEvent>())
            .Returns(new RoutingInfo("test.test-event"));

        var mockLogger = new Mock<ILogger<OutboxPublisher>>();

        IEventPublisher publisher = new OutboxPublisher(
            mockRepo.Object, mockSerializer.Object, mockResolver.Object, mockLogger.Object);

        await publisher.PublishAsync(new TestEvent());

        Assert.NotNull(capturedEntry);
        Assert.Null(capturedEntry!.ScheduledAt);
    }

    // Minimal non-outbox publisher stub
    private sealed class NonOutboxPublisher : IEventPublisher, ICommandSender
    {
        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IEvent => Task.CompletedTask;

        public Task SendAsync<T>(T command, CancellationToken cancellationToken = default)
            where T : ICommand => Task.CompletedTask;

        public Task SendAsync<T>(T command, string queueName, CancellationToken cancellationToken = default)
            where T : ICommand => Task.CompletedTask;
    }
}
