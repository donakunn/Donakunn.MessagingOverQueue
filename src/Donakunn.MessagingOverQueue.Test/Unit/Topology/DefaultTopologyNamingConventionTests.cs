using Donakunn.MessagingOverQueue.Abstractions.Consuming;
using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Topology.Attributes;
using Donakunn.MessagingOverQueue.Topology.Conventions;

namespace MessagingOverQueue.Test.Unit.Topology
{

    public class DefaultTopologyNamingConventionTests
    {
        private readonly DefaultTopologyNamingConvention _sut = new();

        // ── GetStreamKey ───────────────────────────────────────────────────

        [Fact]
        public void GetStreamKey_NoAttribute_UsesNamespaceAndClassName()
        {
            var key = _sut.GetStreamKey(typeof(PlainEvent));
            Assert.Equal("topology.plain", key);
        }

        [Fact]
        public void GetStreamKey_WithName_UsesAttributeName()
        {
            var key = _sut.GetStreamKey(typeof(EventWithName));
            Assert.Equal("topology.order-created", key);
        }

        [Fact]
        public void GetStreamKey_WithNameAndVersion_AppendsVersion()
        {
            var key = _sut.GetStreamKey(typeof(EventWithVersion));
            Assert.Equal("topology.order-created.v1", key);
        }

        [Fact]
        public void GetStreamKey_WithAllFields_CombinesAll()
        {
            var key = _sut.GetStreamKey(typeof(EventWithAllFields));
            Assert.Equal("orders.order-created.v2", key);
        }

        [Fact]
        public void GetStreamKey_NoVersion_OmitsVersionSegment()
        {
            var key = _sut.GetStreamKey(typeof(EventWithCategory));
            Assert.Equal("orders.event-with-category", key);
        }

        [Fact]
        public void GetStreamKey_DoesNotContainDotHyphen()
        {
            var key = _sut.GetStreamKey(typeof(EventWithAllFields));
            Assert.DoesNotContain(".-", key);
        }

        // ── GetConsumerNames — stream key ──────────────────────────────────

        [Fact]
        public void GetConsumerNames_NoAttributes_StreamKeyMatchesGetStreamKey()
        {
            var names = _sut.GetConsumerNames(typeof(PlainHandler), typeof(PlainEvent));
            var publisherKey = _sut.GetStreamKey(typeof(PlainEvent));
            Assert.Equal(publisherKey, names.StreamKey);
        }

        [Fact]
        public void GetConsumerNames_NoConsumerVersion_FallsBackToEventVersion()
        {
            var names = _sut.GetConsumerNames(typeof(PlainHandler), typeof(EventWithVersion));
            Assert.Equal("topology.order-created.v1", names.StreamKey);
        }

        [Fact]
        public void GetConsumerNames_StreamKeyCategoryAlwaysFromEvent()
        {
            // [ConsumerTopology(Category)] only affects consumer group, not stream key category
            var names = _sut.GetConsumerNames(typeof(HandlerWithCategory), typeof(EventWithAllFields));
            Assert.StartsWith("orders.", names.StreamKey);
        }

        // ── GetConsumerNames — consumer group ─────────────────────────────

        [Fact]
        public void GetConsumerNames_NoAttributes_GroupUsesNamespaceAndEventName()
        {
            var names = _sut.GetConsumerNames(typeof(PlainHandler), typeof(PlainEvent));
            // PlainHandler is in namespace MessagingOverQueue.Test.Unit.Topology
            // First non-generic segment: "MessagingOverQueue" → "messaging-over-queue"
            // Event name: "plain"
            Assert.EndsWith(".plain", names.ConsumerGroupName);
        }

        [Fact]
        public void GetConsumerNames_WithServiceName_GroupUsesServiceName()
        {
            var sut = new DefaultTopologyNamingConvention(
                new TopologyNamingOptions { ServiceName = "inventory-service" });
            var names = sut.GetConsumerNames(typeof(PlainHandler), typeof(PlainEvent));
            Assert.StartsWith("inventory-service.", names.ConsumerGroupName);
        }

        [Fact]
        public void GetConsumerNames_ConsumerCategoryOverridesServiceName()
        {
            var names = _sut.GetConsumerNames(typeof(HandlerWithCategory), typeof(PlainEvent));
            Assert.StartsWith("payments.", names.ConsumerGroupName);
        }

        [Fact]
        public void GetConsumerNames_ConsumerNameOverridesEventName()
        {
            var names = _sut.GetConsumerNames(typeof(HandlerWithName), typeof(PlainEvent));
            Assert.EndsWith(".audit-handler", names.ConsumerGroupName);
        }

        [Fact]
        public void GetConsumerNames_AllConsumerFields_FullOverride()
        {
            var sut = new DefaultTopologyNamingConvention(
                new TopologyNamingOptions { ServiceName = "default-service" });
            var names = sut.GetConsumerNames(typeof(HandlerWithAllConsumerFields), typeof(EventWithAllFields));
            Assert.Equal("billing.custom-handler", names.ConsumerGroupName);
        }

        [Fact]
        public void GetConsumerNames_PascalCaseServiceName_KebabCased()
        {
            var names = _sut.GetConsumerNames(
                typeof(InventoryService.HandlerInInventoryService),
                typeof(PlainEvent));
            Assert.StartsWith("inventory-service.", names.ConsumerGroupName);
        }

        // ── GetDeadLetterStreamKey ─────────────────────────────────────────

        [Fact]
        public void GetDeadLetterStreamKey_CombinesStreamKeyAndGroup()
        {
            var dlq = _sut.GetDeadLetterStreamKey("orders.order-created.v1", "inventory-service.order-created");
            Assert.Equal("orders.order-created.v1:inventory-service.order-created:dlq", dlq);
        }

        // ── Test types ─────────────────────────────────────────────────────

        internal class PlainEvent : IMessage
        {
            public Guid Id { get; } = Guid.NewGuid();
            public DateTime Timestamp { get; } = DateTime.UtcNow;
            public string? CorrelationId => null;
            public string? CausationId => null;
            public string MessageType => nameof(PlainEvent);
        }

        [EventTopology(Name = "order-created")]
        private class EventWithName : IMessage
        {
            public Guid Id { get; } = Guid.NewGuid();
            public DateTime Timestamp { get; } = DateTime.UtcNow;
            public string? CorrelationId => null;
            public string? CausationId => null;
            public string MessageType => nameof(EventWithName);
        }

        [EventTopology(Name = "order-created", Version = "v1")]
        private class EventWithVersion : IMessage
        {
            public Guid Id { get; } = Guid.NewGuid();
            public DateTime Timestamp { get; } = DateTime.UtcNow;
            public string? CorrelationId => null;
            public string? CausationId => null;
            public string MessageType => nameof(EventWithVersion);
        }

        [EventTopology(Category = "orders", Name = "order-created", Version = "v2")]
        private class EventWithAllFields : IMessage
        {
            public Guid Id { get; } = Guid.NewGuid();
            public DateTime Timestamp { get; } = DateTime.UtcNow;
            public string? CorrelationId => null;
            public string? CausationId => null;
            public string MessageType => nameof(EventWithAllFields);
        }

        [EventTopology(Category = "orders")]
        private class EventWithCategory : IMessage
        {
            public Guid Id { get; } = Guid.NewGuid();
            public DateTime Timestamp { get; } = DateTime.UtcNow;
            public string? CorrelationId => null;
            public string? CausationId => null;
            public string MessageType => nameof(EventWithCategory);
        }

        private class PlainHandler : IMessageHandler<PlainEvent>
        {
            public Task HandleAsync(PlainEvent m, IMessageContext ctx, CancellationToken ct) => Task.CompletedTask;
        }

        [ConsumerTopology(Category = "payments")]
        private class HandlerWithCategory : IMessageHandler<PlainEvent>
        {
            public Task HandleAsync(PlainEvent m, IMessageContext ctx, CancellationToken ct) => Task.CompletedTask;
        }

        [ConsumerTopology(Name = "audit-handler")]
        private class HandlerWithName : IMessageHandler<PlainEvent>
        {
            public Task HandleAsync(PlainEvent m, IMessageContext ctx, CancellationToken ct) => Task.CompletedTask;
        }

        [ConsumerTopology(Category = "billing", Name = "custom-handler")]
        private class HandlerWithAllConsumerFields : IMessageHandler<EventWithAllFields>
        {
            public Task HandleAsync(EventWithAllFields m, IMessageContext ctx, CancellationToken ct) => Task.CompletedTask;
        }
    }

} // namespace MessagingOverQueue.Test.Unit.Topology

namespace InventoryService
{
    using Donakunn.MessagingOverQueue.Abstractions.Consuming;
    using static MessagingOverQueue.Test.Unit.Topology.DefaultTopologyNamingConventionTests;

    internal class HandlerInInventoryService : IMessageHandler<PlainEvent>
    {
        public Task HandleAsync(PlainEvent m, IMessageContext ctx, CancellationToken ct) => Task.CompletedTask;
    }
}
