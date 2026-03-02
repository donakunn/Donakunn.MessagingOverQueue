using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Abstractions.Publishing;
using Donakunn.MessagingOverQueue.Abstractions.Serialization;
using Donakunn.MessagingOverQueue.Configuration.Options;
using Donakunn.MessagingOverQueue.Persistence;
using Donakunn.MessagingOverQueue.Persistence.Entities;
using Donakunn.MessagingOverQueue.Persistence.Repositories;
using Donakunn.MessagingOverQueue.Topology;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace MessagingOverQueue.Test.Unit.Publishing;

public class DelayedPublishingTests
{
    private record TestMessage : MessageBase { }

    private MessageStoreEntry? _capturedEntry;

    private OutboxPublisher CreateOutboxPublisher(TimeSpan? maxDelay = null)
    {
        var mockRepo = new Mock<IOutboxRepository>();
        mockRepo
            .Setup(r => r.TryAddAsync(It.IsAny<MessageStoreEntry>(), It.IsAny<CancellationToken>()))
            .Callback<MessageStoreEntry, CancellationToken>((e, _) => _capturedEntry = e)
            .ReturnsAsync(true);


        var mockSerializer = new Mock<IMessageSerializer>();
        mockSerializer
            .Setup(s => s.Serialize(It.IsAny<IMessage>()))
            .Returns([]);

        var mockResolver = new Mock<IMessageRoutingResolver>();
        mockResolver
            .Setup(r => r.ResolveRouting<TestMessage>())
            .Returns(new RoutingInfo("test.test-message"));

        var options = new OutboxOptions();
        if (maxDelay.HasValue)
            options.MaxDelay = maxDelay.Value;

        var mockOptions = Options.Create(options);
        var mockLogger = new Mock<ILogger<OutboxPublisher>>();

        return new OutboxPublisher(
            mockRepo.Object, mockSerializer.Object, mockResolver.Object, mockOptions, mockLogger.Object);
    }

    [Fact]
    public async Task IMessagePublisher_PublishWithDelay_ThrowsWhenNotOutbox()
    {
        IMessagePublisher publisher = new NonOutboxPublisher();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => publisher.PublishAsync(new TestMessage(), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task OutboxPublisher_PublishWithDelay_SetsScheduledAt()
    {
        var publisher = CreateOutboxPublisher();
        var before = DateTime.UtcNow;
        var delay = TimeSpan.FromMinutes(10);

        await publisher.PublishAsync(new TestMessage(), delay);

        Assert.NotNull(_capturedEntry);
        Assert.NotNull(_capturedEntry!.ScheduledAt);
        Assert.True(_capturedEntry.ScheduledAt >= before.Add(delay));
        Assert.True(_capturedEntry.ScheduledAt <= DateTime.UtcNow.Add(delay).AddSeconds(1));
    }

    [Fact]
    public async Task PublishWithDelay_NegativeDelay_ThrowsArgumentOutOfRangeException()
    {
        var publisher = CreateOutboxPublisher();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => publisher.PublishAsync(new TestMessage(), TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task PublishWithDelay_ExceedsMaxDelay_ThrowsArgumentOutOfRangeException()
    {
        var publisher = CreateOutboxPublisher(maxDelay: TimeSpan.FromHours(1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => publisher.PublishAsync(new TestMessage(), TimeSpan.FromHours(2)));
    }

    [Fact]
    public async Task PublishWithDelay_ZeroDelay_SetsScheduledAtToNow()
    {
        var before = DateTime.UtcNow;
        var publisher = CreateOutboxPublisher();
        await publisher.PublishAsync(new TestMessage(), TimeSpan.Zero);
        Assert.NotNull(_capturedEntry!.ScheduledAt);
        Assert.True(_capturedEntry.ScheduledAt >= before);
        Assert.True(_capturedEntry.ScheduledAt <= DateTime.UtcNow.AddSeconds(1));
    }

    // Minimal non-outbox publisher stub
    private sealed class NonOutboxPublisher : IMessagePublisher
    {
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
            where T : IMessage => Task.CompletedTask;

        public Task PublishAsync<T>(T message, PublishOptions options, CancellationToken cancellationToken = default)
            where T : IMessage => Task.CompletedTask;
    }
}
