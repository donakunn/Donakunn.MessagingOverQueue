# AsyncronousComunication - RabbitMQ Messaging Library

A robust, high-performance, SOLID-compliant asynchronous messaging library for .NET built on RabbitMQ.

## Features

- **Clean Abstractions**: Simple interfaces for publishing and consuming messages (`ICommand`, `IEvent`, `IQuery`)
- **Fluent Configuration**: Builder pattern for easy configuration of exchanges, queues, and bindings
- **Entity Framework Integration**: Outbox pattern for reliable message delivery with transactional consistency
- **Resilience**: Built-in retry policies, circuit breakers, and dead letter handling
- **Middleware Pipeline**: Extensible pipeline for both publishing and consuming
- **Health Checks**: Built-in ASP.NET Core health check support
- **Dependency Injection**: First-class DI support with Microsoft.Extensions.DependencyInjection

## Installation

Add the package to your project:

```bash
dotnet add package AsyncronousComunication
```

## Quick Start

### 1. Define Your Messages

```csharp
// Command - handled by exactly one consumer
public class CreateOrderCommand : Command
{
    public string CustomerId { get; init; } = string.Empty;
    public List<OrderItem> Items { get; init; } = new();
}

// Event - can be consumed by multiple subscribers
public class OrderCreatedEvent : Event
{
    public Guid OrderId { get; init; }
    public string CustomerId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
}
```

### 2. Configure Services

```csharp
// In Program.cs or Startup.cs
services.AddRabbitMqMessaging(options => options
    .UseHost("localhost")
    .UsePort(5672)
    .WithCredentials("guest", "guest")
    .WithConnectionName("MyApplication")
    .WithChannelPoolSize(10)
    
    // Declare exchanges
    .AddExchange(ex => ex
        .WithName("orders-exchange")
        .AsTopic()
        .Durable())
    
    // Declare queues
    .AddQueue(q => q
        .WithName("orders-queue")
        .Durable()
        .WithDeadLetterExchange("dlx-exchange")
        .WithMessageTtl(TimeSpan.FromHours(24)))
    
    // Declare bindings
    .AddBinding(b => b
        .FromExchange("orders-exchange")
        .ToQueue("orders-queue")
        .WithRoutingKey("orders.#")))
    
    // Add handlers
    .AddHandler<CreateOrderHandler, CreateOrderCommand>()
    .AddHandler<OrderCreatedHandler, OrderCreatedEvent>()
    
    // Add consumers
    .AddConsumer("orders-queue", options => {
        options.PrefetchCount = 10;
        options.MaxConcurrency = 5;
    })
    
    // Add health checks
    .AddHealthChecks();
```

### 3. Create Message Handlers

```csharp
public class CreateOrderHandler : IMessageHandler<CreateOrderCommand>
{
    private readonly IOrderService _orderService;
    private readonly IEventPublisher _eventPublisher;

    public CreateOrderHandler(IOrderService orderService, IEventPublisher eventPublisher)
    {
        _orderService = orderService;
        _eventPublisher = eventPublisher;
    }

    public async Task HandleAsync(
        CreateOrderCommand command, 
        IMessageContext context, 
        CancellationToken cancellationToken)
    {
        // Process the command
        var order = await _orderService.CreateOrderAsync(command, cancellationToken);
        
        // Publish an event
        await _eventPublisher.PublishAsync(new OrderCreatedEvent
        {
            OrderId = order.Id,
            CustomerId = command.CustomerId,
            TotalAmount = order.TotalAmount,
            CorrelationId = context.CorrelationId // Preserve correlation
        }, cancellationToken);
    }
}
```

### 4. Publish Messages

```csharp
public class OrderController : ControllerBase
{
    private readonly ICommandSender _commandSender;
    private readonly IEventPublisher _eventPublisher;

    public OrderController(ICommandSender commandSender, IEventPublisher eventPublisher)
    {
        _commandSender = commandSender;
        _eventPublisher = eventPublisher;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
    {
        // Send a command
        await _commandSender.SendAsync(new CreateOrderCommand
        {
            CustomerId = request.CustomerId,
            Items = request.Items
        });
        
        return Accepted();
    }
}
```

## Outbox Pattern for Reliable Messaging

Use the outbox pattern to ensure messages are published reliably with database transactions:

### 1. Configure Your DbContext

```csharp
public class AppDbContext : DbContext, IOutboxDbContext
{
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;
    public DbSet<InboxMessage> InboxMessages { get; set; } = null!;
    
    // Your domain entities
    public DbSet<Order> Orders { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure outbox tables
        modelBuilder.ConfigureOutbox();
        
        // Your other configurations
    }
}
```

### 2. Register the Outbox Pattern

```csharp
services.AddRabbitMqMessaging(options => { /* ... */ })
    .AddOutboxPattern<AppDbContext>(options =>
    {
        options.ProcessingInterval = TimeSpan.FromSeconds(5);
        options.BatchSize = 100;
        options.RetentionPeriod = TimeSpan.FromDays(7);
    });
```

### 3. Use Transactional Publishing

```csharp
public class OrderService
{
    private readonly AppDbContext _context;
    private readonly OutboxPublisher _outboxPublisher;

    public async Task CreateOrderAsync(CreateOrderCommand command)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        
        try
        {
            // Create order
            var order = new Order { /* ... */ };
            _context.Orders.Add(order);
            
            // Add message to outbox (in same transaction)
            await _outboxPublisher.PublishAsync(new OrderCreatedEvent
            {
                OrderId = order.Id
            });
            
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
```

## Advanced Configuration

### Retry Policies

```csharp
services.AddRabbitMqMessaging(options => { /* ... */ })
    .ConfigureRetry(retry =>
    {
        retry.MaxRetryAttempts = 5;
        retry.InitialDelay = TimeSpan.FromSeconds(1);
        retry.MaxDelay = TimeSpan.FromMinutes(5);
        retry.UseExponentialBackoff = true;
        retry.AddJitter = true;
    });
```

### Circuit Breaker

```csharp
services.AddRabbitMqMessaging(options => { /* ... */ })
    .AddCircuitBreaker(cb =>
    {
        cb.FailureRateThreshold = 0.5;
        cb.SamplingDuration = TimeSpan.FromMinutes(1);
        cb.MinimumThroughput = 10;
        cb.DurationOfBreak = TimeSpan.FromSeconds(30);
    });
```

### Custom Middleware

```csharp
// Publish middleware
public class MetricsMiddleware : IPublishMiddleware
{
    public async Task InvokeAsync(
        PublishContext context, 
        Func<PublishContext, CancellationToken, Task> next, 
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await next(context, cancellationToken);
        sw.Stop();
        
        // Record metrics
        Metrics.RecordPublishDuration(context.MessageType, sw.ElapsedMilliseconds);
    }
}

// Register middleware
services.AddSingleton<IPublishMiddleware, MetricsMiddleware>();
```

### Queue Types

```csharp
// Quorum queue for high availability
.AddQueue(q => q
    .WithName("important-queue")
    .AsQuorumQueue())

// Stream queue for high throughput
.AddQueue(q => q
    .WithName("events-stream")
    .AsStreamQueue())

// Lazy queue for large queues
.AddQueue(q => q
    .WithName("batch-queue")
    .AsLazyQueue())
```

## Message Context

Access message metadata in your handlers:

```csharp
public async Task HandleAsync(MyCommand command, IMessageContext context, CancellationToken ct)
{
    Console.WriteLine($"Message ID: {context.MessageId}");
    Console.WriteLine($"Correlation ID: {context.CorrelationId}");
    Console.WriteLine($"Queue: {context.QueueName}");
    Console.WriteLine($"Delivery Count: {context.DeliveryCount}");
    Console.WriteLine($"Received At: {context.ReceivedAt}");
    
    // Access custom headers
    var customHeader = context.Headers["x-custom-header"];
}
```

## Health Checks

The library provides built-in health checks:

```csharp
app.MapHealthChecks("/health");
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Application                               │
├─────────────────────────────────────────────────────────────────┤
│  ICommandSender  │  IEventPublisher  │  IMessageHandler<T>      │
├─────────────────────────────────────────────────────────────────┤
│                     Middleware Pipeline                          │
│  (Logging, Serialization, Retry, Metrics, etc.)                 │
├─────────────────────────────────────────────────────────────────┤
│  RabbitMqPublisher  │  RabbitMqConsumer  │  OutboxPublisher     │
├─────────────────────────────────────────────────────────────────┤
│                   RabbitMqConnectionPool                         │
├─────────────────────────────────────────────────────────────────┤
│                        RabbitMQ                                  │
└─────────────────────────────────────────────────────────────────┘
```

## License

MIT

