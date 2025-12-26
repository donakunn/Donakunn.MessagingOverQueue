using AsyncronousComunication.Abstractions.Consuming;
using AsyncronousComunication.Abstractions.Messages;
using AsyncronousComunication.Abstractions.Publishing;
using AsyncronousComunication.Abstractions.Serialization;
using AsyncronousComunication.Configuration.Builders;
using AsyncronousComunication.Configuration.Options;
using AsyncronousComunication.Connection;
using AsyncronousComunication.Consuming.Middleware;
using AsyncronousComunication.HealthChecks;
using AsyncronousComunication.Hosting;
using AsyncronousComunication.Persistence;
using AsyncronousComunication.Persistence.Repositories;
using AsyncronousComunication.Publishing;
using AsyncronousComunication.Publishing.Middleware;
using AsyncronousComunication.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AsyncronousComunication.DependencyInjection;

/// <summary>
/// Extension methods for registering RabbitMQ messaging services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds RabbitMQ messaging services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for RabbitMQ options.</param>
    /// <returns>The messaging builder for further configuration.</returns>
    public static IMessagingBuilder AddRabbitMqMessaging(
        this IServiceCollection services,
        Action<RabbitMqOptionsBuilder> configure)
    {
        var builder = new RabbitMqOptionsBuilder();
        configure(builder);
        var options = builder.Build();

        services.Configure<RabbitMqOptions>(opt =>
        {
            opt.HostName = options.HostName;
            opt.Port = options.Port;
            opt.UserName = options.UserName;
            opt.Password = options.Password;
            opt.VirtualHost = options.VirtualHost;
            opt.UseSsl = options.UseSsl;
            opt.SslServerName = options.SslServerName;
            opt.ClientProvidedName = options.ClientProvidedName;
            opt.ConnectionTimeout = options.ConnectionTimeout;
            opt.RequestedHeartbeat = options.RequestedHeartbeat;
            opt.ChannelPoolSize = options.ChannelPoolSize;
            opt.AutomaticRecoveryEnabled = options.AutomaticRecoveryEnabled;
            opt.NetworkRecoveryInterval = options.NetworkRecoveryInterval;
            opt.Exchanges = options.Exchanges;
            opt.Queues = options.Queues;
            opt.Bindings = options.Bindings;
        });

        // Core services
        services.TryAddSingleton<IRabbitMqConnectionPool, RabbitMqConnectionPool>();
        services.TryAddSingleton<IMessageSerializer, JsonMessageSerializer>();
        services.TryAddSingleton<IMessageTypeResolver, MessageTypeResolver>();

        // Publishers
        services.TryAddSingleton<RabbitMqPublisher>();
        services.TryAddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<RabbitMqPublisher>());
        services.TryAddSingleton<IEventPublisher>(sp => sp.GetRequiredService<RabbitMqPublisher>());
        services.TryAddSingleton<ICommandSender>(sp => sp.GetRequiredService<RabbitMqPublisher>());

        // Publish middleware
        services.AddSingleton<IPublishMiddleware, LoggingMiddleware>();
        services.AddSingleton<IPublishMiddleware, SerializationMiddleware>();

        // Consume middleware
        services.AddSingleton<IConsumeMiddleware, ConsumeLoggingMiddleware>();
        services.AddSingleton<IConsumeMiddleware, DeserializationMiddleware>();

        // Resilience
        services.Configure<RetryOptions>(_ => { });
        services.TryAddSingleton<IRetryPolicy, PollyRetryPolicy>();

        // Hosted services
        services.AddHostedService<RabbitMqHostedService>();

        return new MessagingBuilder(services);
    }

    /// <summary>
    /// Adds RabbitMQ messaging with default configuration.
    /// </summary>
    public static IMessagingBuilder AddRabbitMqMessaging(this IServiceCollection services)
    {
        return services.AddRabbitMqMessaging(_ => { });
    }

    /// <summary>
    /// Adds the outbox pattern for reliable messaging.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that implements IOutboxDbContext.</typeparam>
    /// <param name="builder">The messaging builder.</param>
    /// <param name="configure">Optional configuration for outbox options.</param>
    /// <returns>The messaging builder for chaining.</returns>
    public static IMessagingBuilder AddOutboxPattern<TContext>(
        this IMessagingBuilder builder,
        Action<OutboxOptions>? configure = null)
        where TContext : DbContext, IOutboxDbContext
    {
        var services = builder.Services;

        services.Configure<OutboxOptions>(options =>
        {
            configure?.Invoke(options);
        });

        // Register outbox repository
        services.AddScoped<IOutboxRepository, OutboxRepository<TContext>>();

        // Register outbox publisher as the default publisher for transactional scenarios
        services.AddScoped<OutboxPublisher>();

        // Register outbox processor
        services.AddHostedService<OutboxProcessor>();

        return builder;
    }

    /// <summary>
    /// Adds a message handler to the service collection.
    /// </summary>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="builder">The messaging builder.</param>
    /// <returns>The messaging builder for chaining.</returns>
    public static IMessagingBuilder AddHandler<THandler, TMessage>(this IMessagingBuilder builder)
        where THandler : class, IMessageHandler<TMessage>
        where TMessage : IMessage
    {
        builder.Services.AddScoped<IMessageHandler<TMessage>, THandler>();
        
        // Register the message type for resolution
        builder.Services.AddSingleton<IMessageTypeRegistration>(new MessageTypeRegistration<TMessage>());
        
        return builder;
    }

    /// <summary>
    /// Adds a consumer for a specific queue.
    /// </summary>
    /// <param name="builder">The messaging builder.</param>
    /// <param name="queueName">The queue to consume from.</param>
    /// <param name="configure">Optional consumer configuration.</param>
    /// <returns>The messaging builder for chaining.</returns>
    public static IMessagingBuilder AddConsumer(
        this IMessagingBuilder builder,
        string queueName,
        Action<ConsumerOptions>? configure = null)
    {
        var options = new ConsumerOptions { QueueName = queueName };
        configure?.Invoke(options);

        builder.Services.AddSingleton(new ConsumerRegistration { Options = options });
        
        // Ensure consumer hosted service is registered
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, ConsumerHostedService>());

        return builder;
    }

    /// <summary>
    /// Adds RabbitMQ health checks.
    /// </summary>
    /// <param name="builder">The messaging builder.</param>
    /// <param name="name">The health check name.</param>
    /// <param name="failureStatus">The failure status.</param>
    /// <param name="tags">Optional tags.</param>
    /// <returns>The messaging builder for chaining.</returns>
    public static IMessagingBuilder AddHealthChecks(
        this IMessagingBuilder builder,
        string name = "rabbitmq",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        builder.Services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                name,
                sp => sp.GetRequiredService<RabbitMqHealthCheck>(),
                failureStatus,
                tags));

        builder.Services.TryAddSingleton<RabbitMqHealthCheck>();

        return builder;
    }

    /// <summary>
    /// Configures retry options.
    /// </summary>
    public static IMessagingBuilder ConfigureRetry(
        this IMessagingBuilder builder,
        Action<RetryOptions> configure)
    {
        builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>
    /// Adds a circuit breaker.
    /// </summary>
    public static IMessagingBuilder AddCircuitBreaker(
        this IMessagingBuilder builder,
        Action<CircuitBreakerOptions>? configure = null)
    {
        var options = new CircuitBreakerOptions();
        configure?.Invoke(options);
        
        builder.Services.AddSingleton<ICircuitBreaker>(new PollyCircuitBreaker(options));
        return builder;
    }
}

/// <summary>
/// Builder interface for configuring messaging services.
/// </summary>
public interface IMessagingBuilder
{
    /// <summary>
    /// The service collection.
    /// </summary>
    IServiceCollection Services { get; }
}

/// <summary>
/// Default implementation of IMessagingBuilder.
/// </summary>
internal class MessagingBuilder : IMessagingBuilder
{
    public IServiceCollection Services { get; }

    public MessagingBuilder(IServiceCollection services)
    {
        Services = services;
    }
}

/// <summary>
/// Interface for message type registrations.
/// </summary>
internal interface IMessageTypeRegistration
{
    void Register(IMessageTypeResolver resolver);
}

/// <summary>
/// Registration for a message type.
/// </summary>
internal class MessageTypeRegistration<TMessage> : IMessageTypeRegistration where TMessage : IMessage
{
    public void Register(IMessageTypeResolver resolver)
    {
        resolver.RegisterType<TMessage>();
    }
}

