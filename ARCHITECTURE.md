# MessagingOverQueue - Architecture & Technical Documentation

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Core Components](#core-components)
- [Configuration System](#configuration-system)
- [Topology Management](#topology-management)
- [Message Pipeline](#message-pipeline)
- [Resilience Patterns](#resilience-patterns)
- [Outbox Pattern](#outbox-pattern)
- [Connection Management](#connection-management)
- [Extension Points](#extension-points)

---

## Architecture Overview

The library follows a layered architecture with clean separation of concerns:

```
???????????????????????????????????????????????????????????????????????
?                    Application Layer                                 ?
?  (ICommandSender, IEventPublisher, IMessageHandler<T>)              ?
???????????????????????????????????????????????????????????????????????
?                   Middleware Pipeline Layer                          ?
?  Publishing: Logging ? Serialization ? Topology Resolution          ?
?  Consuming: Logging ? Deserialization ? Idempotency ? Retry         ?
???????????????????????????????????????????????????????????????????????
?                     Topology Layer                                   ?
?  Auto-Discovery, Convention-Based Naming, Registry                  ?
???????????????????????????????????????????????????????????????????????
?                Infrastructure Layer                                  ?
?  RabbitMqPublisher, RabbitMqConsumer, OutboxPublisher               ?
???????????????????????????????????????????????????????????????????????
?                 Configuration Layer                                  ?
?  Sources: Aspire ? AppSettings ? Fluent API ? Custom                ?
???????????????????????????????????????????????????????????????????????
?                   Connection Pool                                    ?
?  RabbitMqConnectionPool (Channel Pooling & Lifecycle)               ?
???????????????????????????????????????????????????????????????????????
?                      RabbitMQ Broker                                 ?
???????????????????????????????????????????????????????????????????????
```

### Design Principles

1. **SOLID Principles**
   - Single Responsibility: Each class has one clear purpose
   - Open/Closed: Extensible via middleware and configuration sources
   - Liskov Substitution: Interfaces can be substituted with implementations
   - Interface Segregation: Fine-grained interfaces (ICommandSender, IEventPublisher)
   - Dependency Inversion: Depends on abstractions, not concretions

2. **Dependency Injection First**
   - All services registered via `IServiceCollection`
   - Scoped services for transactional operations (Outbox)
   - Singleton services for shared infrastructure (ConnectionPool)

3. **Asynchronous from the Ground Up**
   - All I/O operations are async
   - CancellationToken support throughout
   - Async disposal with `IAsyncDisposable`

---

## Core Components

### 1. Message Abstractions

**Location**: `src/Abstractions/Messages/`

#### IMessage Interface
```csharp
public interface IMessage
{
    Guid Id { get; }
    DateTime Timestamp { get; }
    string? CorrelationId { get; }
    string? CausationId { get; }
    string MessageType { get; }
}
```

#### Message Types
- **ICommand**: Point-to-point messages (one handler)
- **IEvent**: Pub/sub messages (multiple subscribers)
- **IQuery<TResult>**: Request/response pattern

#### MessageBase
Abstract base class providing:
- Auto-generated `Id` (Guid)
- UTC `Timestamp`
- Correlation tracking
- Cloning with correlation IDs

### 2. Publishers

**Location**: `src/Publishing/`

#### RabbitMqPublisher
The core publisher implementing:
- `IMessagePublisher` - Generic publishing
- `IEventPublisher` - Event-specific
- `ICommandSender` - Command-specific

**Key Features**:
- Middleware pipeline integration
- Topology-aware routing via `IMessageRoutingResolver`
- Channel pooling for concurrency
- Automatic serialization

**Publishing Flow**:
```
Message ? Middleware Pipeline ? Serialize ? Resolve Topology ? 
Acquire Channel ? Publish to Exchange ? Return Channel
```

#### OutboxPublisher
Transactional publishing via outbox pattern:
- Stores messages in database
- Part of same transaction as domain changes
- Background processor publishes messages
- Ensures at-least-once delivery

### 3. Consumers

**Location**: `src/Consuming/`

#### RabbitMqConsumer
Manages message consumption:
- Dedicated channel per consumer
- Message acknowledgment
- Prefetch and concurrency control via `SemaphoreSlim`
- Middleware pipeline execution
- Processing timeout with cancellation

**Consumption Flow**:
```
Receive from Queue ? Acquire Semaphore ? Middleware Pipeline ? Deserialize ? 
Handler Resolution ? Execute Handler ? ACK/NACK ? Release Semaphore
```

#### ConsumerHostedService
Manages multiple consumers:
- Lifecycle management
- Waits for topology initialization via `TopologyReadySignal`
- Parallel consumer startup
- Graceful shutdown

### 4. Message Handlers

**Location**: `src/Abstractions/Consuming/`

```csharp
public interface IMessageHandler<in TMessage> where TMessage : IMessage
{
    Task HandleAsync(
        TMessage message, 
        IMessageContext context, 
        CancellationToken cancellationToken);
}
```

**IMessageContext** provides:
- Message metadata (ID, timestamp, delivery count)
- Queue and routing information
- Custom headers
- Correlation tracking

### 5. Handler Invoker System

**Location**: `src/Consuming/Handlers/`

The handler invoker system provides reflection-free message dispatch:

#### IHandlerInvoker
```csharp
public interface IHandlerInvoker
{
    Type MessageType { get; }
    Task InvokeAsync(
        IServiceProvider serviceProvider,
        IMessage message,
        IMessageContext context,
        CancellationToken cancellationToken);
}
```

#### HandlerInvoker<TMessage>
```csharp
internal sealed class HandlerInvoker<TMessage> : IHandlerInvoker
    where TMessage : IMessage
{
    public Type MessageType => typeof(TMessage);

    public async Task InvokeAsync(
        IServiceProvider serviceProvider,
        IMessage message,
        IMessageContext context,
        CancellationToken cancellationToken)
    {
        // Resolve ALL handlers for this message type (supports multiple handlers)
        var handlers = serviceProvider.GetServices<IMessageHandler<TMessage>>();

        foreach (var handler in handlers)
        {
            // Strongly-typed call - no reflection, no boxing
            await handler.HandleAsync((TMessage)message, context, cancellationToken);
        }
    }
}
```

#### HandlerInvokerRegistry
```csharp
public sealed class HandlerInvokerRegistry : IHandlerInvokerRegistry
{
    private readonly ConcurrentDictionary<Type, IHandlerInvoker> _invokers = new();

    public IHandlerInvoker? GetInvoker(Type messageType)
    {
        return _invokers.TryGetValue(messageType, out var invoker) ? invoker : null;
    }

    public void Register(IHandlerInvoker invoker)
    {
        _invokers.TryAdd(invoker.MessageType, invoker);
    }
}
```

---

## Configuration System

### Architecture

The configuration system uses a **Composer Pattern** with prioritized sources:

```
RabbitMqConfigurationComposer
??? AspireConfigurationSource (Priority: 25)
??? AppSettingsConfigurationSource (Priority: 50)
??? FluentConfigurationSource (Priority: 100)
??? Custom Sources (Configurable Priority)
```

### Configuration Sources

**Location**: `src/Configuration/Sources/`

#### 1. IRabbitMqConfigurationSource
```csharp
public interface IRabbitMqConfigurationSource
{
    int Priority { get; }
    void Configure(RabbitMqOptions options);
}
```

#### 2. AspireConfigurationSource
- Reads from .NET Aspire service discovery
- Connection string format: `amqp://user:pass@host:port/vhost`
- Default priority: 25 (lowest)
- Ideal for local development

#### 3. AppSettingsConfigurationSource
- Binds from `IConfiguration`
- Supports environment variables
- Default section: "RabbitMq"
- Priority: 50
- Production-ready

#### 4. FluentConfigurationSource
- Builder pattern API
- Runtime configuration
- Highest priority: 100
- Override other sources

### Configuration Merge Strategy

```csharp
public RabbitMqOptions Build()
{
    var options = new RabbitMqOptions();
    
    // Apply sources in priority order (lowest to highest)
    foreach (var source in _sources.OrderBy(s => s.Priority))
    {
        source.Configure(options);
    }
    
    return options;
}
```

### Builders

**Location**: `src/Configuration/Builders/`

#### RabbitMqOptionsBuilder
Fluent API for connection and topology:
```csharp
.UseHost("localhost")
.UsePort(5672)
.WithCredentials("user", "pass")
.WithConnectionName("MyApp")
.WithChannelPoolSize(20)
```

---

## Topology Management

### Overview

Topology management provides **automatic infrastructure provisioning** from handler discovery.

### Components

**Location**: `src/Topology/`

#### 1. ITopologyScanner
Scans assemblies for handlers and message types:

```csharp
public interface ITopologyScanner
{
    IReadOnlyCollection<MessageTypeInfo> ScanForMessageTypes(params Assembly[] assemblies);
    IReadOnlyCollection<HandlerTypeInfo> ScanForHandlers(params Assembly[] assemblies);
    IReadOnlyCollection<HandlerTopologyInfo> ScanForHandlerTopology(params Assembly[] assemblies);
}
```

#### 2. ITopologyNamingConvention
Generates names from message types:

```csharp
public interface ITopologyNamingConvention
{
    string GetExchangeName(Type messageType);
    string GetQueueName(Type messageType);
    string GetRoutingKey(Type messageType);
    string GetDeadLetterExchangeName(string sourceQueueName);
    string GetDeadLetterQueueName(string sourceQueueName);
}
```

**DefaultTopologyNamingConvention**:
- Events: `events.{message-name}` (topic exchange)
- Commands: `commands.{message-name}` (direct exchange)
- Queues: `{service-name}.{message-name}`
- Routing keys: `{namespace}.{message-name}`

#### 3. ITopologyRegistry
Thread-safe registry for topology metadata:
- Exchange definitions
- Queue definitions
- Binding rules
- Routing key patterns

#### 4. ITopologyProvider
Provides topology from message types:

```csharp
public interface ITopologyProvider
{
    TopologyMetadata GetTopologyForMessage(Type messageType);
}
```

#### 5. ITopologyDeclarer
Declares topology on RabbitMQ:

```csharp
public interface ITopologyDeclarer
{
    Task DeclareTopologyAsync(TopologyMetadata metadata, CancellationToken cancellationToken);
    Task DeclareExchangeAsync(ExchangeDefinition definition, CancellationToken cancellationToken);
    Task DeclareQueueAsync(QueueDefinition definition, CancellationToken cancellationToken);
    Task DeclareBindingAsync(BindingDefinition definition, CancellationToken cancellationToken);
}
```

### Topology Attributes

**Location**: `src/Topology/Attributes/`

#### ConsumerQueueAttribute (Handler-level)
```csharp
[ConsumerQueue(
    Name = "custom-queue",
    QueueType = QueueType.Quorum,
    PrefetchCount = 20,
    MaxConcurrency = 5)]
public class MyHandler : IMessageHandler<MyEvent> { }
```

#### MessageAttribute
```csharp
[Message(AutoDiscover = false, Version = "2.0", Group = "orders")]
public class OrderCreatedEvent : Event { }
```

#### ExchangeAttribute
```csharp
[Exchange("orders-exchange", Type = ExchangeType.Topic, Durable = true)]
public class OrderCreatedEvent : Event { }
```

#### QueueAttribute
```csharp
[Queue("orders-queue", QueueType = QueueType.Quorum, MessageTtlMs = 86400000)]
public class OrderCreatedEvent : Event { }
```

#### RoutingKeyAttribute
```csharp
[RoutingKey("orders.created.{version}")]
public class OrderCreatedEvent : Event { }
```

#### DeadLetterAttribute
```csharp
[DeadLetter("orders-dlx", QueueName = "orders-failed")]
public class OrderCreatedEvent : Event { }
```

### Topology Auto-Discovery Flow

```
Application Start
    ?
TopologyInitializationHostedService
    ?
TopologyScanner.ScanForHandlerTopology()
    ?
For Each Handler Found:
    ??? Register Handler in DI (scoped)
    ??? Create HandlerInvoker<TMessage>
    ??? Register in HandlerInvokerRegistry
    ??? Register Message Type for Serialization
    ??? Register ConsumerRegistration
    ?
Build TopologyDefinitions from Handler + Message Attributes
    ?
TopologyDeclarer.DeclareAsync() ? RabbitMQ
    ?
TopologyReadySignal.SetReady()
    ?
ConsumerHostedService Starts Consumers
    ?
Ready for Message Processing
```

### TopologyBuilder (Fluent API)

**Location**: `src/Topology/Builders/TopologyBuilder.cs`

```csharp
services.AddRabbitMqMessaging(config)
    .AddTopology(topology => topology
        .WithServiceName("order-service")
        .WithDeadLetterEnabled(true)
        .ConfigureNaming(naming =>
        {
            naming.UseLowerCase = true;
            naming.EventExchangePrefix = "events.";
        })
        .ConfigureProvider(provider =>
        {
            provider.DefaultDurable = true;
        })
        .ScanAssemblyContaining<OrderCreatedHandler>());
```

---

## Message Pipeline

### Publisher Pipeline

**Location**: `src/Publishing/Middleware/`

**Interface**: `IPublishMiddleware`

```csharp
public interface IPublishMiddleware
{
    Task InvokeAsync(
        PublishContext context, 
        Func<PublishContext, CancellationToken, Task> next, 
        CancellationToken cancellationToken);
}
```

**Built-in Middleware**:
1. **LoggingMiddleware**: Logs publish operations
2. **SerializationMiddleware**: Serializes message to bytes

**Execution Order**:
```
IMessagePublisher.PublishAsync()
    ? LoggingMiddleware
        ? SerializationMiddleware
            ? RabbitMqPublisher.PublishToRabbitMqAsync()
```

### Consumer Pipeline

**Location**: `src/Consuming/Middleware/`

**Interface**: `IConsumeMiddleware`

```csharp
public interface IConsumeMiddleware
{
    Task InvokeAsync(
        ConsumeContext context, 
        Func<ConsumeContext, CancellationToken, Task> next, 
        CancellationToken cancellationToken);
}
```

**Built-in Middleware**:
1. **ConsumeLoggingMiddleware**: Logs consumption
2. **DeserializationMiddleware**: Deserializes bytes to message
3. **IdempotencyMiddleware**: Prevents duplicate processing (with Outbox)
4. **RetryMiddleware**: Handles failures with retry logic

**Execution Order**:
```
RabbitMQ Delivery
    ? ConsumeLoggingMiddleware
        ? DeserializationMiddleware
            ? IdempotencyMiddleware
                ? RetryMiddleware
                    ? Handler Invocation
```

### Custom Middleware Example

```csharp
public class MetricsMiddleware : IPublishMiddleware
{
    private readonly IMetrics _metrics;

    public async Task InvokeAsync(
        PublishContext context, 
        Func<PublishContext, CancellationToken, Task> next, 
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await next(context, cancellationToken);
            _metrics.RecordSuccess(context.MessageType, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _metrics.RecordFailure(context.MessageType, sw.Elapsed, ex);
            throw;
        }
    }
}

// Register
services.AddSingleton<IPublishMiddleware, MetricsMiddleware>();
```

---

## Resilience Patterns

**Location**: `src/Resilience/`

### 1. Retry Policy

**Implementation**: Polly-based retry with:
- Exponential backoff
- Jitter to prevent thundering herd
- Maximum retry attempts
- Configurable delays

```csharp
services.ConfigureRetry(retry =>
{
    retry.MaxRetryAttempts = 5;
    retry.InitialDelay = TimeSpan.FromSeconds(1);
    retry.MaxDelay = TimeSpan.FromMinutes(5);
    retry.UseExponentialBackoff = true;
    retry.AddJitter = true;
});
```

### 2. Circuit Breaker

**Location**: `src/Resilience/CircuitBreaker/`

**States**: Closed ? Open ? Half-Open ? Closed

```csharp
services.AddCircuitBreaker(cb =>
{
    cb.FailureRateThreshold = 0.5; // 50% failures
    cb.SamplingDuration = TimeSpan.FromMinutes(1);
    cb.MinimumThroughput = 10; // Min calls before breaking
    cb.DurationOfBreak = TimeSpan.FromSeconds(30);
});
```

### 3. Dead Letter Handling

Automatic DLX configuration via topology:
- Retry exhausted messages
- Poison message isolation

---

## Outbox Pattern

**Location**: `src/Persistence/`

### Architecture

```
Application Transaction
    ?
Domain Changes + OutboxMessage (same transaction)
    ?
Commit
    ?
OutboxProcessor (background)
    ?
Read Outbox ? Publish to RabbitMQ ? Mark Processed
```

### Components

#### 1. IOutboxDbContext
```csharp
public interface IOutboxDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<InboxMessage> InboxMessages { get; }
}
```

#### 2. OutboxMessage Entity
```csharp
public class OutboxMessage
{
    public Guid Id { get; set; }
    public string MessageType { get; set; }
    public byte[] Payload { get; set; }
    public string? RoutingKey { get; set; }
    public string? ExchangeName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? Error { get; set; }
}
```

#### 3. OutboxPublisher
Scoped service for transactional publishing.

#### 4. OutboxProcessor
Background hosted service:
- Polls outbox table at intervals
- Publishes pending messages
- Updates processed status
- Handles failures with retry

---

## Connection Management

**Location**: `src/Connection/`

### RabbitMqConnectionPool

**Features**:
- Single connection per application
- Channel pooling for concurrency
- Dedicated channels for consumers
- Automatic reconnection
- Thread-safe channel management

**Architecture**:
```
Application
    ?
RabbitMqConnectionPool
    ??? IConnection (singleton)
    ??? Channel Pool
        ??? Pooled Channels (for publishing)
        ??? Dedicated Channels (for consumers)
```

**Key Methods**:
```csharp
public interface IRabbitMqConnectionPool
{
    Task<IChannel> GetChannelAsync(CancellationToken ct);
    void ReturnChannel(IChannel channel);
    Task<IChannel> CreateDedicatedChannelAsync(CancellationToken ct);
    Task EnsureConnectedAsync(CancellationToken ct);
    ValueTask DisposeAsync();
}
```

---

## Extension Points

### 1. Custom Configuration Sources

Implement `IRabbitMqConfigurationSource`:
```csharp
public class VaultConfigurationSource : IRabbitMqConfigurationSource
{
    public int Priority => 75;
    
    public void Configure(RabbitMqOptions options)
    {
        options.HostName = _vault.GetSecret("rabbitmq-host");
        options.Password = _vault.GetSecret("rabbitmq-password");
    }
}
```

### 2. Custom Serialization

Implement `IMessageSerializer`:
```csharp
public class ProtobufSerializer : IMessageSerializer
{
    public byte[] Serialize<T>(T message) where T : IMessage { }
    public T Deserialize<T>(byte[] data) where T : IMessage { }
}

services.AddSingleton<IMessageSerializer, ProtobufSerializer>();
```

### 3. Custom Topology Naming

Implement `ITopologyNamingConvention`:
```csharp
public class CustomNamingConvention : ITopologyNamingConvention
{
    public string GetExchangeName(Type messageType)
    {
        return $"myapp.{messageType.Name.ToLower()}";
    }
    // Implement other methods...
}

services.AddSingleton<ITopologyNamingConvention, CustomNamingConvention>();
```

### 4. Custom Middleware

See [Message Pipeline](#message-pipeline) section.

### 5. Custom Message Type Resolution

Implement `IMessageTypeResolver`:
```csharp
public class CustomTypeResolver : IMessageTypeResolver
{
    public void RegisterType(Type messageType) { }
    public Type? ResolveType(string messageType) { }
}
```

---

## Performance Considerations

### 1. Handler Invoker Pattern
- O(1) lookup via `ConcurrentDictionary`
- Reflection only at startup
- Strongly-typed invocation (no `MethodInfo.Invoke`)

### 2. Channel Pooling
- Channels are expensive to create
- Pooled channels for publishing
- Dedicated channels for consumers

### 3. Prefetch Count
- Controls how many messages are buffered
- Higher = better throughput, higher memory
- Lower = better distribution, lower latency

### 4. Concurrency Control
- `SemaphoreSlim` in `RabbitMqConsumer`
- Configurable via `MaxConcurrency`
- Prevents resource exhaustion

### 5. Queue Types

| Type | Use Case | Pros | Cons |
|------|----------|------|------|
| Classic | General purpose | Flexible, well-tested | No HA guarantees |
| Quorum | High availability | Replicated, consistent | Higher resource usage |
| Stream | High throughput | Fast, scalable | Limited features |
| Lazy | Large queues | Memory efficient | Slower access |

---

## Testing Strategies

### 1. Unit Testing Handlers
```csharp
[Fact]
public async Task Handler_ProcessesMessage_Successfully()
{
    var handler = new OrderCreatedHandler(_orderService, _eventPublisher);
    var message = new OrderCreatedEvent { OrderId = Guid.NewGuid() };
    var context = new MessageContext { /* ... */ };
    
    await handler.HandleAsync(message, context, CancellationToken.None);
    
    _orderService.Verify(x => x.ProcessOrder(message.OrderId));
}
```

### 2. Integration Testing with TestContainers
```csharp
public class RabbitMqTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder().Build();
    
    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }
    
    [Fact]
    public async Task PublishConsume_EndToEnd_Works()
    {
        var services = new ServiceCollection();
        services.AddRabbitMqMessaging(options => options
            .UseHost(_container.Hostname)
            .UsePort(_container.GetMappedPublicPort(5672)));
        
        // Test publish and consume
    }
}
```

---

## Best Practices

### 1. Configuration
- Use Aspire for local development
- Use appsettings.json for deployment config
- Use fluent API for runtime overrides
- Store secrets in Azure Key Vault or similar

### 2. Message Design
- Keep messages immutable (init-only properties)
- Include correlation IDs for tracing
- Version messages for schema evolution
- Use record types for conciseness

### 3. Handler Design
- Keep handlers small and focused
- Use DI for dependencies
- Handle CancellationToken properly
- Log important events

### 4. Error Handling
- Use dead letter queues for poison messages
- Implement retry with exponential backoff
- Monitor dead letter queues
- Alert on high failure rates

### 5. Topology Management
- Use handler-based auto-discovery
- Review generated topology before production
- Document custom configurations
- Use `[ConsumerQueue]` for performance tuning

### 6. Performance
- Tune prefetch count per queue
- Monitor queue depths
- Use appropriate queue types
- Scale consumers horizontally

---
