# MessagingOverQueue

[![GitHub Repository](https://img.shields.io/badge/GitHub-donakunn%2FDonakunn.MessagingOverQueue-blue?logo=github)](https://github.com/donakunn/Donakunn.MessagingOverQueue)
[![.NET 10](https://img.shields.io/badge/.NET-10-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-Apache%202.0-green)](LICENSE)

Async messaging library for .NET 10 with Redis Streams. Implement `IMessageHandler<T>` and the library creates streams, consumer groups, and consumers automatically.

- Handler auto-discovery via assembly scanning
- Reflection-free handler dispatch with O(1) lookup
- Middleware pipeline (retry, circuit breaker, timeout, idempotency)
- Outbox pattern with SQL Server provider
- Delayed message delivery — schedule events and commands for future processing
- Scoped DI — each message gets its own handler instance

## Installation

```bash
dotnet add package Donakunn.MessagingOverQueue.RedisStreams
```

## Quick Start

### 1. Define a message

```csharp
using Donakunn.MessagingOverQueue.Abstractions.Messages;

public record OrderCreatedEvent : Event
{
    public Guid OrderId { get; init; }
    public string CustomerId { get; init; } = string.Empty;
}
```

### 2. Create a handler

```csharp
using Donakunn.MessagingOverQueue.Abstractions.Consuming;
using Donakunn.MessagingOverQueue.Abstractions.Messages;

public class OrderCreatedHandler : IMessageHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedHandler> _logger;

    public OrderCreatedHandler(ILogger<OrderCreatedHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(
        OrderCreatedEvent message,
        IMessageContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Order {OrderId} created", message.OrderId);
        return Task.CompletedTask;
    }
}
```

### 3. Configure services

```csharp
using Donakunn.MessagingOverQueue.RedisStreams.DependencyInjection;
using Donakunn.MessagingOverQueue.Topology.DependencyInjection;

services.AddRedisStreamsMessaging(options => options
    .UseConnectionString("localhost:6379")
    .WithStreamPrefix("myapp"))
    .AddTopology(topology => topology
        .WithServiceName("order-service")
        .ScanAssemblyContaining<OrderCreatedHandler>())
    .AddRedisStreamsConsumerHostedService();
```

The library automatically discovers your handlers, creates Redis streams and consumer groups, registers handlers in DI with scoped lifetime, and starts consuming.

## Publishing

Inject `IEventPublisher` or `ICommandSender`:

```csharp
public class OrderController(IEventPublisher publisher) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
    {
        await publisher.PublishAsync(new OrderCreatedEvent
        {
            OrderId = Guid.NewGuid(),
            CustomerId = request.CustomerId
        });

        return Accepted();
    }
}
```

### Delayed Publishing

Schedule a message for future delivery by passing a `TimeSpan` delay. Requires the outbox to be configured.

```csharp
// Deliver in 10 minutes
await publisher.PublishAsync(new OrderReminderEvent { OrderId = id }, TimeSpan.FromMinutes(10));

// Send a command after a delay
await sender.SendAsync(new ExpireSessionCommand { UserId = userId }, TimeSpan.FromHours(1));
```

Calling the delay overload without the outbox configured throws `NotSupportedException` immediately, making misconfiguration explicit at the call site.

## Features

**Handler Discovery** — `TopologyScanner` finds all `IMessageHandler<T>` implementations at startup. Streams, consumer groups, and consumers are created based on conventions or attributes. Override the consumer group with `[RedisConsumerGroup("custom-group")]`.

**Reflection-Free Dispatch** — `HandlerInvoker<T>` instances are created once at startup and cached in a `ConcurrentDictionary`. Runtime dispatch is a dictionary lookup + direct method call.

**Middleware Pipeline** — Extensible pipeline for both publishing and consuming. Built-in consume middlewares (in execution order):

| Order | Middleware | Purpose |
|-------|-----------|---------|
| 100 | CircuitBreaker | Fail fast when downstream is unhealthy |
| 200 | Retry | Automatic retry with exponential backoff |
| 300 | Timeout | Cancel long-running handlers |
| 400 | Logging | Structured logging |
| 500 | Idempotency | Duplicate detection via inbox pattern |
| 600 | Deserialization | JSON to strongly-typed message |

**Resilience** — Retry, circuit breaker, and timeout powered by Polly v8. Configure via `UseResilience`:

```csharp
builder.UseResilience(r => r
    .WithRetry(opts => opts.MaxRetryAttempts = 5)
    .WithCircuitBreaker(opts => opts.FailureThreshold = 10)
    .WithTimeout(TimeSpan.FromSeconds(30)));
```

**Outbox Pattern** — Reliable message delivery with SQL Server (ADO.NET, no EF Core dependency). Supports partition-based horizontal scaling with multiple workers, and delayed delivery via `ScheduledAt`. The `EnsureSchemaAsync` migration is idempotent — existing tables gain the `ScheduledAt` column automatically. Configure via `UsePersistence`:

```csharp
builder.UsePersistence(p => p
    .WithOutbox(opts => opts.BatchSize = 50)
        .UseSqlServer(connectionString)
    .WithIdempotency());
```

**Dead Letter Handling** — Messages exceeding max delivery attempts are moved to DLQ streams. Configurable per-stream or per-consumer-group.

**Stream Retention** — Time-based (`MINID` trimming) or count-based (`MAXLEN` trimming).

**Health Checks** — Built-in ASP.NET Core health check. Reports connection status, latency, and Redis server version.

**Configuration** — Fluent API, `appsettings.json` (section: `"RedisStreams"`), or both.

## Full Configuration Example

```csharp
services.AddMessaging()
    .UseRedisStreamsQueues(queues => queues
        .WithConnection(opts => opts
            .UseConnectionString("localhost:6379")
            .WithStreamPrefix("myapp")
            .ConfigureConsumer(batchSize: 20, maxPendingMessages: 1000)
            .ConfigureClaiming(claimIdleTime: TimeSpan.FromMinutes(5))
            .WithTimeBasedRetention(TimeSpan.FromDays(7))
            .WithDeadLetterPerConsumerGroup(maxDeliveryAttempts: 5))
        .WithTopology(t => t
            .WithServiceName("order-service")
            .ScanAssemblyContaining<OrderCreatedHandler>())
        .WithHealthChecks())
    .UseResilience(r => r
        .WithRetry(opts => opts.MaxRetryAttempts = 5)
        .WithCircuitBreaker()
        .WithTimeout(TimeSpan.FromSeconds(30)))
    .UsePersistence(p => p
        .WithOutbox(opts => opts.BatchSize = 50)
            .UseSqlServer(connectionString)
        .WithIdempotency());
```

## Requirements

- .NET 10+
- Redis 6.2+ (for `XAUTOCLAIM`)
- SQL Server 2016+ (outbox provider, optional)

## License

Apache 2.0

## Contributing

Contributions are welcome. Visit the [GitHub repository](https://github.com/donakunn/Donakunn.MessagingOverQueue) to report issues, submit pull requests, or request features.