using Donakunn.MessagingOverQueue.DependencyInjection;
using Donakunn.MessagingOverQueue.Persistence.Entities;
using Donakunn.MessagingOverQueue.Persistence.Repositories;
using Donakunn.MessagingOverQueue.Topology.DependencyInjection;
using MessagingOverQueue.Test.Integration.Infrastructure;
using MessagingOverQueue.Test.Integration.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Donakunn.MessagingOverQueue.Topology.DependencyInjection.TopologyServiceCollectionExtensions;

namespace MessagingOverQueue.Test.Integration;

/// <summary>
/// Integration tests for the idempotency pattern using inbox messages.
/// Tests that duplicate messages are not processed multiple times.
/// </summary>
public class IdempotencyIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task InboxRepository_Marks_Message_As_Processed()
    {
        // Arrange
        using var host = await BuildHostWithIdempotency();
        var testEvent = new SimpleTestEvent { Value = "IdempotencyTest" };
        var handlerType = typeof(SimpleTestEventHandler).FullName!;

        // Act - mark message as processed
        using (var scope = host.Services.CreateScope())
        {
            var inboxRepository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
            await inboxRepository.MarkAsProcessedAsync(testEvent, handlerType);
        }

        // Assert - verify message is in inbox
        using (var scope = host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var inboxMessage = await dbContext.InboxMessages
                .FirstOrDefaultAsync(m => m.Id == testEvent.Id && m.HandlerType == handlerType);

            Assert.NotNull(inboxMessage);
            Assert.Equal(testEvent.MessageType, inboxMessage.MessageType);
            Assert.True(inboxMessage.ProcessedAt <= DateTime.UtcNow);
        }
    }

    [Fact]
    public async Task InboxRepository_HasBeenProcessed_Returns_True_For_Processed_Message()
    {
        // Arrange
        using var host = await BuildHostWithIdempotency();
        var testEvent = new SimpleTestEvent { Value = "CheckProcessed" };
        var handlerType = typeof(SimpleTestEventHandler).FullName!;

        // Mark as processed first
        using (var scope = host.Services.CreateScope())
        {
            var inboxRepository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
            await inboxRepository.MarkAsProcessedAsync(testEvent, handlerType);
        }

        // Act & Assert
        using (var scope = host.Services.CreateScope())
        {
            var inboxRepository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
            var hasBeenProcessed = await inboxRepository.HasBeenProcessedAsync(testEvent.Id, handlerType);

            Assert.True(hasBeenProcessed);
        }
    }

    [Fact]
    public async Task InboxRepository_HasBeenProcessed_Returns_False_For_New_Message()
    {
        // Arrange
        using var host = await BuildHostWithIdempotency();
        var testEvent = new SimpleTestEvent { Value = "NewMessage" };
        var handlerType = typeof(SimpleTestEventHandler).FullName!;

        // Act & Assert - check without marking as processed
        using (var scope = host.Services.CreateScope())
        {
            var inboxRepository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
            var hasBeenProcessed = await inboxRepository.HasBeenProcessedAsync(testEvent.Id, handlerType);

            Assert.False(hasBeenProcessed);
        }
    }

    [Fact]
    public async Task InboxRepository_Same_Message_Different_Handlers_Are_Independent()
    {
        // Arrange
        using var host = await BuildHostWithIdempotency();
        var testEvent = new SimpleTestEvent { Value = "MultiHandler" };
        var handlerTypeA = "HandlerA";
        var handlerTypeB = "HandlerB";

        // Mark as processed by HandlerA only
        using (var scope = host.Services.CreateScope())
        {
            var inboxRepository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
            await inboxRepository.MarkAsProcessedAsync(testEvent, handlerTypeA);
        }

        // Act & Assert
        using (var scope = host.Services.CreateScope())
        {
            var inboxRepository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();

            var processedByA = await inboxRepository.HasBeenProcessedAsync(testEvent.Id, handlerTypeA);
            var processedByB = await inboxRepository.HasBeenProcessedAsync(testEvent.Id, handlerTypeB);

            Assert.True(processedByA);
            Assert.False(processedByB);
        }
    }

    [Fact]
    public async Task InboxRepository_Cleanup_Removes_Old_Processed_Messages()
    {
        // Arrange
        using var host = await BuildHostWithIdempotency();
        var oldMessageId = Guid.NewGuid();

        // Create an old inbox message directly in database
        using (var scope = host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var oldMessage = new InboxMessage
            {
                Id = oldMessageId,
                MessageType = "TestType",
                HandlerType = "TestHandler",
                ProcessedAt = DateTime.UtcNow.AddDays(-10)
            };
            dbContext.InboxMessages.Add(oldMessage);
            await dbContext.SaveChangesAsync();
        }

        // Act - cleanup with 1 day retention
        using (var scope = host.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
            await repository.CleanupAsync(TimeSpan.FromDays(1));
        }

        // Assert
        using (var scope = host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var message = await dbContext.InboxMessages
                .FirstOrDefaultAsync(m => m.Id == oldMessageId);
            Assert.Null(message);
        }
    }

    [Fact]
    public async Task InboxRepository_Cleanup_Preserves_Recent_Messages()
    {
        // Arrange
        using var host = await BuildHostWithIdempotency();
        var recentEvent = new SimpleTestEvent { Value = "Recent" };
        var handlerType = "TestHandler";

        // Mark as processed (will have recent ProcessedAt timestamp)
        using (var scope = host.Services.CreateScope())
        {
            var inboxRepository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
            await inboxRepository.MarkAsProcessedAsync(recentEvent, handlerType);
        }

        // Act - cleanup with 1 day retention
        using (var scope = host.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
            await repository.CleanupAsync(TimeSpan.FromDays(1));
        }

        // Assert - recent message should still exist
        using (var scope = host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var message = await dbContext.InboxMessages
                .FirstOrDefaultAsync(m => m.Id == recentEvent.Id && m.HandlerType == handlerType);
            Assert.NotNull(message);
        }
    }

    [Fact]
    public async Task InboxMessage_Preserves_CorrelationId()
    {
        // Arrange
        using var host = await BuildHostWithIdempotency();
        var correlationId = Guid.NewGuid().ToString();
        var testEvent = new SimpleTestEvent { Value = "CorrelationTest" };

        // Set correlation ID via reflection
        typeof(SimpleTestEvent).GetProperty("CorrelationId")?.SetValue(testEvent, correlationId);

        var handlerType = "TestHandler";

        // Act
        using (var scope = host.Services.CreateScope())
        {
            var inboxRepository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
            await inboxRepository.MarkAsProcessedAsync(testEvent, handlerType);
        }

        // Assert
        using (var scope = host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var inboxMessage = await dbContext.InboxMessages
                .FirstOrDefaultAsync(m => m.Id == testEvent.Id && m.HandlerType == handlerType);

            Assert.NotNull(inboxMessage);
            Assert.Equal(correlationId, inboxMessage.CorrelationId);
        }
    }

    [Fact]
    public async Task Multiple_Handlers_Can_Process_Same_Message_Independently()
    {
        // Arrange
        using var host = await BuildHostWithIdempotency();
        var testEvent = new MultiHandlerEvent { Payload = "MultiHandlerIdempotency" };

        var handlerTypeA = typeof(MultiHandlerEventHandlerA).FullName!;
        var handlerTypeB = typeof(MultiHandlerEventHandlerB).FullName!;

        // Act - mark as processed by both handlers
        using (var scope = host.Services.CreateScope())
        {
            var inboxRepository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
            await inboxRepository.MarkAsProcessedAsync(testEvent, handlerTypeA);
            await inboxRepository.MarkAsProcessedAsync(testEvent, handlerTypeB);
        }

        // Assert - both records should exist
        using (var scope = host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var inboxMessages = await dbContext.InboxMessages
                .Where(m => m.Id == testEvent.Id)
                .ToListAsync();

            Assert.Equal(2, inboxMessages.Count);
            Assert.Contains(inboxMessages, m => m.HandlerType == handlerTypeA);
            Assert.Contains(inboxMessages, m => m.HandlerType == handlerTypeB);
        }
    }

    [Fact]
    public async Task Idempotency_Middleware_Prevents_Duplicate_Processing_Via_RabbitMq()
    {
        // Arrange
        IdempotentTestEventHandler.Reset();
        using var host = await BuildHostWithIdempotency();
        var testEvent = new IdempotentTestEvent { Value = "DuplicateTest" };

        // Pre-mark the message as processed to simulate a duplicate
        using (var scope = host.Services.CreateScope())
        {
            var inboxRepository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
            await inboxRepository.MarkAsProcessedAsync(
                testEvent,
                typeof(IdempotentTestEventHandler).FullName!);
        }

        // Act - publish the "duplicate" message
        using (var scope = host.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<Donakunn.MessagingOverQueue.Abstractions.Publishing.IEventPublisher>();
            await publisher.PublishAsync(testEvent);
        }

        // Wait a short time for message to be consumed (or not)
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert - handler should NOT have been called because message was already processed
        Assert.Equal(0, IdempotentTestEventHandler.HandleCount);
    }

    [Fact]
    public async Task Idempotency_Middleware_Allows_First_Time_Processing()
    {
        // Arrange
        IdempotentTestEventHandler.Reset();
        using var host = await BuildHostWithIdempotency();
        var testEvent = new IdempotentTestEvent { Value = "FirstTimeProcessing" };

        // Act - publish without pre-marking as processed
        using (var scope = host.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<Donakunn.MessagingOverQueue.Abstractions.Publishing.IEventPublisher>();
            await publisher.PublishAsync(testEvent);
        }

        // Wait for message to be processed
        await IdempotentTestEventHandler.WaitForCountAsync(1, TimeSpan.FromSeconds(15));

        // Assert - handler should have been called
        Assert.Equal(1, IdempotentTestEventHandler.HandleCount);
        Assert.Contains(IdempotentTestEventHandler.HandledMessages, m => m.Value == "FirstTimeProcessing");
    }

    [Fact]
    public async Task Idempotency_Middleware_Records_Processed_Message_In_Inbox()
    {
        // Arrange
        IdempotentTestEventHandler.Reset();
        using var host = await BuildHostWithIdempotency();
        var testEvent = new IdempotentTestEvent { Value = "RecordInInbox" };

        // Act - publish message
        using (var scope = host.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<Donakunn.MessagingOverQueue.Abstractions.Publishing.IEventPublisher>();
            await publisher.PublishAsync(testEvent);
        }

        // Wait for message to be processed
        await IdempotentTestEventHandler.WaitForCountAsync(1, TimeSpan.FromSeconds(15));

        // Assert - message should be recorded in inbox
        using (var scope = host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var inboxMessage = await dbContext.InboxMessages
                .FirstOrDefaultAsync(m => m.Id == testEvent.Id);

            Assert.NotNull(inboxMessage);
            Assert.Equal(typeof(IdempotentTestEventHandler).FullName, inboxMessage.HandlerType);
        }
    }

    [Fact]
    public async Task Republishing_Same_Message_Is_Idempotent()
    {
        // Arrange
        IdempotentTestEventHandler.Reset();
        using var host = await BuildHostWithIdempotency();
        var testEvent = new IdempotentTestEvent { Value = "RepublishTest" };

        // Act - publish the same message multiple times
        using (var scope = host.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<Donakunn.MessagingOverQueue.Abstractions.Publishing.IEventPublisher>();

            await publisher.PublishAsync(testEvent);
            await publisher.PublishAsync(testEvent);
            await publisher.PublishAsync(testEvent);
        }

        // Wait for first message to be processed
        await IdempotentTestEventHandler.WaitForCountAsync(1, TimeSpan.FromSeconds(15));

        // Give some time for any additional messages to be processed
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert - handler should only have been called once
        Assert.Equal(1, IdempotentTestEventHandler.HandleCount);
    }

    private async Task<IHost> BuildHostWithIdempotency()
    {
        return await BuildHost(services =>
        {
            // Add DbContext with SQL Server
            services.AddDbContext<TestDbContext>(options =>
                options.UseSqlServer(ConnectionString));

            services.AddRabbitMqMessaging(options =>
            {
                options.UseHost(_rabbitMqContainer!.Hostname);
                options.UsePort(_rabbitMqContainer!.GetMappedPublicPort(5672));
                options.WithCredentials("guest", "guest");
                options.WithConnectionName($"IdempotencyTests-{Guid.NewGuid():N}");
            })
            .AddTopology(topology => topology
                .WithServiceName("idempotency-test-service")
                .ScanAssemblyContaining<IdempotentTestEventHandler>())
            .AddOutboxPattern<TestDbContext>();
        });
    }

    protected override void ConfigureAdditionalServices(IServiceCollection services)
    {
        // No additional services needed - base class handles DbContext registration
    }
}
