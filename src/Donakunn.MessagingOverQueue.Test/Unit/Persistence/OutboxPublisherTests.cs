using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Abstractions.Serialization;
using Donakunn.MessagingOverQueue.Configuration.Options;
using Donakunn.MessagingOverQueue.Persistence;
using Donakunn.MessagingOverQueue.Persistence.Entities;
using Donakunn.MessagingOverQueue.Persistence.Repositories;
using Donakunn.MessagingOverQueue.Topology;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace MessagingOverQueue.Test.Unit.Persistence;

public class OutboxPublisherTests
{
    private record TestMessage : MessageBase;

    [Fact]
    public async Task PublishAsync_AddsEntryToRepository()
    {
        var mockRepository = new Mock<IOutboxRepository>();
        var mockSerializer = new Mock<IMessageSerializer>();
        var mockResolver = new Mock<IMessageRoutingResolver>();

        mockSerializer
            .Setup(s => s.Serialize(It.IsAny<IMessage>()))
            .Returns(new byte[] { 1, 2, 3 });
        mockResolver
            .Setup(r => r.ResolveRouting<TestMessage>())
            .Returns(new RoutingInfo("test-stream"));

        var publisher = new OutboxPublisher(
            mockRepository.Object,
            mockSerializer.Object,
            mockResolver.Object,
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxPublisher>.Instance);

        await publisher.PublishAsync(new TestMessage());

        mockRepository.Verify(
            r => r.AddAsync(It.IsAny<MessageStoreEntry>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
