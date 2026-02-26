using Donakunn.MessagingOverQueue.Abstractions.Consuming;
using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Topology.Attributes;
using Donakunn.MessagingOverQueue.Topology.Conventions;

namespace MessagingOverQueue.Test.Unit.Topology
{

public class DefaultTopologyNamingConventionTests
{
    private readonly DefaultTopologyNamingConvention _sut = new();

    [Fact]
    public void GetExchangeType_Event_ReturnsTopic()
    {
        Assert.Equal("topic", _sut.GetExchangeType(typeof(SampleEvent)));
    }

    [Fact]
    public void GetExchangeType_Command_ReturnsDirect()
    {
        Assert.Equal("direct", _sut.GetExchangeType(typeof(SampleCommand)));
    }

    [Fact]
    public void GetExchangeType_Generic_ReturnsTopic()
    {
        Assert.Equal("topic", _sut.GetExchangeType(typeof(SampleMessage)));
    }

    [Fact]
    public void GetExchangeName_Event_NoDotHyphenInName()
    {
        var name = _sut.GetExchangeName(typeof(SampleEvent));
        Assert.DoesNotContain(".-", name);
        Assert.Equal("events.sample", name);
    }

    [Fact]
    public void GetExchangeName_Command_NoDotHyphenInName()
    {
        var name = _sut.GetExchangeName(typeof(SampleCommand));
        Assert.DoesNotContain(".-", name);
        Assert.Equal("commands.sample", name);
    }

    [Fact]
    public void GetExchangeName_MultiWordEvent_CorrectKebabCase()
    {
        var name = _sut.GetExchangeName(typeof(OrderCreatedEvent));
        Assert.Equal("events.order-created", name);
    }

    [Fact]
    public void GetDeadLetterExchangeName_ReturnsExpected()
    {
        Assert.Equal("dlx.inventory-service.order-created", _sut.GetDeadLetterExchangeName("inventory-service.order-created"));
    }

    [Fact]
    public void GetDeadLetterQueueName_ReturnsExpected()
    {
        Assert.Equal("inventory-service.order-created.dlq", _sut.GetDeadLetterQueueName("inventory-service.order-created"));
    }

    [Fact]
    public void GetConsumerQueueName_HandlerInPascalCaseNamespace_KebabCasedPrefix()
    {
        var queue = _sut.GetConsumerQueueName(
            typeof(InventoryService.Handlers.SampleHandlerInInventoryService),
            typeof(SampleEvent));
        Assert.StartsWith("inventory-service.", queue);
    }

    // Task 3 tests — EventTopologyAttribute

    [Fact]
    public void GetExchangeName_WithEventTopologyName_UsesAttributeName()
    {
        var name = _sut.GetExchangeName(typeof(EventWithTopologyName));
        Assert.Equal("events.payment-processed", name);
    }

    [Fact]
    public void GetExchangeName_WithEventTopologyNameAndVersion_AppendsVersion()
    {
        var name = _sut.GetExchangeName(typeof(EventWithTopologyVersion));
        Assert.Equal("events.payment-processed.v2", name);
    }

    [Fact]
    public void GetRoutingKey_WithEventTopologyCategory_UsesCategoryFromAttribute()
    {
        var key = _sut.GetRoutingKey(typeof(CategoryOnlyEvent));
        Assert.StartsWith("payments.", key);
    }

    [Fact]
    public void GetRoutingKey_WithAllEventTopologyFields_UsesAllAttributeValues()
    {
        var key = _sut.GetRoutingKey(typeof(EventWithAllTopologyFields));
        Assert.Equal("payments.payment-processed.v2", key);
    }

    [Fact]
    public void GetQueueName_WithEventTopologyName_UsesAttributeNameAsMessageSegment()
    {
        var queue = _sut.GetQueueName(typeof(EventWithTopologyName));
        Assert.EndsWith(".payment-processed", queue);
    }

    // Task 4 tests — ConsumerTopologyAttribute

    [Fact]
    public void GetConsumerQueueName_WithConsumerCategory_UsesCategoryAsPrefix()
    {
        var queue = _sut.GetConsumerQueueName(typeof(HandlerWithCategory), typeof(SampleEvent));
        Assert.Equal("inventory.sample", queue);
    }

    [Fact]
    public void GetConsumerQueueName_WithConsumerName_UsesNameAsMessageSegment()
    {
        var queue = _sut.GetConsumerQueueName(typeof(HandlerWithName), typeof(SampleEvent));
        Assert.EndsWith(".my-consumer", queue);
    }

    [Fact]
    public void GetConsumerQueueName_WithConsumerVersion_OverridesEventVersion()
    {
        var queue = _sut.GetConsumerQueueName(typeof(HandlerWithVersion), typeof(EventWithTopologyVersion));
        Assert.EndsWith(".v3", queue);
        Assert.DoesNotContain(".v2", queue);
    }

    [Fact]
    public void GetConsumerQueueName_NoConsumerVersion_FallsBackToEventVersion()
    {
        var queue = _sut.GetConsumerQueueName(typeof(HandlerWithCategory), typeof(EventWithTopologyVersion));
        Assert.EndsWith(".v2", queue);
    }

    [Fact]
    public void GetConsumerQueueName_AllFieldsSet_FullOverride()
    {
        var queue = _sut.GetConsumerQueueName(typeof(HandlerWithAllFields), typeof(EventWithAllTopologyFields));
        Assert.Equal("inventory.my-consumer.v3", queue);
    }

    #region Test Types

    public class SampleEvent : IEvent
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; } = null;
        public string? CausationId { get; } = null;
        public string MessageType { get; } = nameof(SampleEvent);
    }

    public class SampleCommand : ICommand
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; } = null;
        public string? CausationId { get; } = null;
        public string MessageType { get; } = nameof(SampleCommand);
    }

    public class SampleMessage : IMessage
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; } = null;
        public string? CausationId { get; } = null;
        public string MessageType { get; } = nameof(SampleMessage);
    }

    public class OrderCreatedEvent : IEvent
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; } = null;
        public string? CausationId { get; } = null;
        public string MessageType { get; } = nameof(OrderCreatedEvent);
    }

    [EventTopology(Name = "payment-processed")]
    public class EventWithTopologyName : IEvent
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; } = null;
        public string? CausationId { get; } = null;
        public string MessageType { get; } = nameof(EventWithTopologyName);
    }

    [EventTopology(Name = "payment-processed", Version = "v2")]
    public class EventWithTopologyVersion : IEvent
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; } = null;
        public string? CausationId { get; } = null;
        public string MessageType { get; } = nameof(EventWithTopologyVersion);
    }

    [EventTopology(Category = "payments")]
    public class CategoryOnlyEvent : IEvent
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; } = null;
        public string? CausationId { get; } = null;
        public string MessageType { get; } = nameof(CategoryOnlyEvent);
    }

    [EventTopology(Category = "payments", Name = "payment-processed", Version = "v2")]
    public class EventWithAllTopologyFields : IEvent
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; } = null;
        public string? CausationId { get; } = null;
        public string MessageType { get; } = nameof(EventWithAllTopologyFields);
    }

    [ConsumerTopology(Category = "inventory")]
    public class HandlerWithCategory : IMessageHandler<SampleEvent>
    {
        public Task HandleAsync(SampleEvent message, IMessageContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [ConsumerTopology(Name = "my-consumer")]
    public class HandlerWithName : IMessageHandler<SampleEvent>
    {
        public Task HandleAsync(SampleEvent message, IMessageContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [ConsumerTopology(Version = "v3")]
    public class HandlerWithVersion : IMessageHandler<SampleEvent>
    {
        public Task HandleAsync(SampleEvent message, IMessageContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [ConsumerTopology(Category = "inventory", Name = "my-consumer", Version = "v3")]
    public class HandlerWithAllFields : IMessageHandler<SampleEvent>
    {
        public Task HandleAsync(SampleEvent message, IMessageContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    #endregion
}

} // namespace MessagingOverQueue.Test.Unit.Topology

namespace InventoryService.Handlers
{
    using Donakunn.MessagingOverQueue.Abstractions.Consuming;

    internal class SampleHandlerInInventoryService
        : IMessageHandler<MessagingOverQueue.Test.Unit.Topology.DefaultTopologyNamingConventionTests.SampleEvent>
    {
        public Task HandleAsync(
            MessagingOverQueue.Test.Unit.Topology.DefaultTopologyNamingConventionTests.SampleEvent message,
            IMessageContext context,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
