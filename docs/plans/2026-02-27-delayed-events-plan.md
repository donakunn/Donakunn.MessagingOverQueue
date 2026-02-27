# Delayed Events Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a `TimeSpan delay` overload to `IEventPublisher` and `ICommandSender` so callers can schedule messages for future delivery using the existing SQL Server outbox.

**Architecture:** Add `ScheduledAt DateTime?` to `MessageStoreEntry` and the SQL schema. The outbox `AcquireOutboxLockAsync` query gets a one-line filter that ignores rows whose `ScheduledAt` is in the future. `OutboxPublisher` implements the new overloads by setting that field. Default interface implementations throw `NotSupportedException` so non-outbox publishers fail fast.

**Tech Stack:** .NET 10, xUnit 2.9, Moq 4, ADO.NET (no EF), SQL Server via Testcontainers for integration tests.

---

## Context you need before starting

- Design doc: `docs/plans/2026-02-27-delayed-events-design.md`
- `IEventPublisher` and `ICommandSender` live together in `src/Donakunn.MessagingOverQueue/Abstractions/Publishing/IEventPublisher.cs`
- `MessageStoreEntry` is in `src/Donakunn.MessagingOverQueue/Persistence/Entities/MessageStoreEntry.cs`
- `OutboxPublisher` is in `src/Donakunn.MessagingOverQueue/Persistence/OutboxPublisher.cs`
- All SQL logic is in one file: `src/Donakunn.MessagingOverQueue/Persistence/Providers/SqlServer/SqlServerMessageStoreProvider.cs`
- `MapEntry` reads columns by **ordinal position** (0–16 currently). Every SELECT query that feeds `MapEntry` must have `ScheduledAt` at position 17.
- Unit tests: `src/Donakunn.MessagingOverQueue.Test/Unit/Persistence/`
- Integration tests: `src/Donakunn.MessagingOverQueue.Test/Integration/RedisStreams/`
- Run tests with: `dotnet test src/Donakunn.MessagingOverQueue.Test/`

---

### Task 1: Add `ScheduledAt` to `MessageStoreEntry`

**Files:**
- Modify: `src/Donakunn.MessagingOverQueue/Persistence/Entities/MessageStoreEntry.cs:93`

**Step 1: Write the failing test**

In `src/Donakunn.MessagingOverQueue.Test/Unit/Persistence/OutboxProcessorTests.cs`, add at the bottom of the class:

```csharp
[Fact]
public void CreateOutboxEntry_WithScheduledAt_SetsField()
{
    var scheduledAt = DateTime.UtcNow.AddMinutes(10);

    var entry = MessageStoreEntry.CreateOutboxEntry(
        id: Guid.NewGuid(),
        messageType: "TestEvent",
        payload: [],
        exchangeName: null,
        routingKey: null,
        queueName: "test.queue",
        headers: null,
        correlationId: null,
        scheduledAt: scheduledAt);

    Assert.Equal(scheduledAt, entry.ScheduledAt);
}

[Fact]
public void CreateOutboxEntry_WithoutScheduledAt_LeavesFieldNull()
{
    var entry = MessageStoreEntry.CreateOutboxEntry(
        id: Guid.NewGuid(),
        messageType: "TestEvent",
        payload: [],
        exchangeName: null,
        routingKey: null,
        queueName: "test.queue",
        headers: null,
        correlationId: null);

    Assert.Null(entry.ScheduledAt);
}
```

**Step 2: Run to verify it fails**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ --filter "CreateOutboxEntry_WithScheduledAt_SetsField|CreateOutboxEntry_WithoutScheduledAt_LeavesFieldNull" -v
```
Expected: compile error — `CreateOutboxEntry` has no `scheduledAt` parameter.

**Step 3: Add `ScheduledAt` property to `MessageStoreEntry`**

In `MessageStoreEntry.cs`, after the `CorrelationId` property (line ~93), add:

```csharp
/// <summary>
/// When the message should become visible for processing (outbox only).
/// Null means immediately eligible.
/// </summary>
public DateTime? ScheduledAt { get; set; }
```

Then update `CreateOutboxEntry` — add an optional parameter and set the field:

```csharp
public static MessageStoreEntry CreateOutboxEntry(
    Guid id,
    string messageType,
    byte[] payload,
    string? exchangeName,
    string? routingKey,
    string? queueName,
    string? headers,
    string? correlationId,
    DateTime? scheduledAt = null)   // ← new optional parameter
{
    return new MessageStoreEntry
    {
        Id = id,
        Direction = MessageDirection.Outbox,
        MessageType = messageType,
        Payload = payload,
        ExchangeName = exchangeName,
        RoutingKey = routingKey,
        QueueName = queueName,
        Headers = headers,
        HandlerType = string.Empty,
        CorrelationId = correlationId,
        CreatedAt = DateTime.UtcNow,
        Status = MessageStatus.Pending,
        RetryCount = 0,
        ScheduledAt = scheduledAt   // ← new
    };
}
```

**Step 4: Run tests to verify they pass**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ --filter "CreateOutboxEntry_WithScheduledAt_SetsField|CreateOutboxEntry_WithoutScheduledAt_LeavesFieldNull" -v
```
Expected: PASS.

**Step 5: Commit**

```bash
git add src/Donakunn.MessagingOverQueue/Persistence/Entities/MessageStoreEntry.cs \
        src/Donakunn.MessagingOverQueue.Test/Unit/Persistence/OutboxProcessorTests.cs
git commit -m "feat: add ScheduledAt field to MessageStoreEntry"
```

---

### Task 2: Add delay overloads to `IEventPublisher` and `ICommandSender`

**Files:**
- Modify: `src/Donakunn.MessagingOverQueue/Abstractions/Publishing/IEventPublisher.cs`

**Step 1: Write the failing test**

Create a new file `src/Donakunn.MessagingOverQueue.Test/Unit/Publishing/DelayedPublishingTests.cs`:

```csharp
using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Abstractions.Publishing;

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
```

**Step 2: Run to verify it fails**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ --filter "IEventPublisher_PublishWithDelay_ThrowsWhenNotOutbox|ICommandSender_SendWithDelay_ThrowsWhenNotOutbox" -v
```
Expected: compile error — `PublishAsync(event, TimeSpan)` overload does not exist.

**Step 3: Add default overloads to `IEventPublisher` and `ICommandSender`**

In `IEventPublisher.cs`, add to `IEventPublisher` after the existing `PublishAsync`:

```csharp
/// <summary>
/// Schedules an event for delivery after the specified delay.
/// Requires outbox persistence to be configured.
/// </summary>
/// <exception cref="NotSupportedException">Thrown when outbox persistence is not configured.</exception>
Task PublishAsync<T>(T @event, TimeSpan delay, CancellationToken cancellationToken = default)
    where T : IEvent
    => throw new NotSupportedException(
        "Delayed publishing requires outbox persistence. Call UsePersistence().WithOutbox() during setup.");
```

Add to `ICommandSender` after the existing `SendAsync` overloads:

```csharp
/// <summary>
/// Schedules a command for delivery after the specified delay.
/// Requires outbox persistence to be configured.
/// </summary>
/// <exception cref="NotSupportedException">Thrown when outbox persistence is not configured.</exception>
Task SendAsync<T>(T command, TimeSpan delay, CancellationToken cancellationToken = default)
    where T : ICommand
    => throw new NotSupportedException(
        "Delayed sending requires outbox persistence. Call UsePersistence().WithOutbox() during setup.");
```

**Step 4: Run tests to verify they pass**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ --filter "IEventPublisher_PublishWithDelay_ThrowsWhenNotOutbox|ICommandSender_SendWithDelay_ThrowsWhenNotOutbox" -v
```
Expected: PASS.

**Step 5: Run the full suite to check nothing broke**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ -v
```
Expected: all previously passing tests still pass.

**Step 6: Commit**

```bash
git add src/Donakunn.MessagingOverQueue/Abstractions/Publishing/IEventPublisher.cs \
        src/Donakunn.MessagingOverQueue.Test/Unit/Publishing/DelayedPublishingTests.cs
git commit -m "feat: add default delay overloads to IEventPublisher and ICommandSender"
```

---

### Task 3: Implement delay overloads in `OutboxPublisher`

**Files:**
- Modify: `src/Donakunn.MessagingOverQueue/Persistence/OutboxPublisher.cs`

**Step 1: Write the failing tests**

Add to `src/Donakunn.MessagingOverQueue.Test/Unit/Publishing/DelayedPublishingTests.cs`:

```csharp
// Add these usings at the top of the file:
// using Donakunn.MessagingOverQueue.Persistence;
// using Donakunn.MessagingOverQueue.Persistence.Entities;
// using Donakunn.MessagingOverQueue.Persistence.Repositories;
// using Donakunn.MessagingOverQueue.Abstractions.Serialization;
// using Donakunn.MessagingOverQueue.Topology;
// using Moq;

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
        .Returns(new MessageRouting("events.test-event", "test.test-event", "test.test-event", "events.test-event"));

    var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<OutboxPublisher>>();

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
        .Returns(new MessageRouting("events.test-event", "test.test-event", "test.test-event", "events.test-event"));

    var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<OutboxPublisher>>();

    IEventPublisher publisher = new OutboxPublisher(
        mockRepo.Object, mockSerializer.Object, mockResolver.Object, mockLogger.Object);

    await publisher.PublishAsync(new TestEvent());

    Assert.NotNull(capturedEntry);
    Assert.Null(capturedEntry!.ScheduledAt);
}
```

**Step 2: Run to verify they fail**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ --filter "OutboxPublisher_PublishWithDelay_SetsScheduledAt|OutboxPublisher_PublishWithoutDelay_LeavesScheduledAtNull" -v
```
Expected: FAIL — `OutboxPublisher` does not implement `PublishAsync(event, TimeSpan)`.

**Step 3: Implement delay overloads in `OutboxPublisher`**

In `OutboxPublisher.cs`, add these two methods. They follow the exact same pattern as the existing `PublishAsync<T>(T @event, CancellationToken)` — the only difference is passing `scheduledAt` to `CreateOutboxEntry`:

```csharp
public Task PublishAsync<T>(T @event, TimeSpan delay, CancellationToken cancellationToken = default)
    where T : IEvent
{
    var routing = _routingResolver.ResolveRouting<T>();
    return PublishDelayedAsync(@event, routing.ExchangeName, routing.RoutingKey, delay, cancellationToken);
}

public Task SendAsync<T>(T command, TimeSpan delay, CancellationToken cancellationToken = default)
    where T : ICommand
{
    var routing = _routingResolver.ResolveRouting<T>();
    return PublishDelayedAsync(command, string.Empty, routing.StreamKey, delay, cancellationToken);
}

private async Task PublishDelayedAsync<T>(
    T message,
    string? exchangeName,
    string? routingKey,
    TimeSpan delay,
    CancellationToken cancellationToken)
    where T : IMessage
{
    var routing = _routingResolver.ResolveRouting<T>();
    var queueName = routing.StreamKey;
    var scheduledAt = DateTime.UtcNow.Add(delay);

    var entry = MessageStoreEntry.CreateOutboxEntry(
        message.Id,
        message.MessageType,
        _serializer.Serialize(message),
        exchangeName,
        routingKey,
        queueName,
        options: null,
        correlationId: message.CorrelationId,
        scheduledAt: scheduledAt);

    await _repository.AddAsync(entry, cancellationToken);

    _logger.LogDebug(
        "Scheduled message {MessageId} for delivery at {ScheduledAt} (delay: {Delay})",
        message.Id, scheduledAt, delay);
}
```

> **Note:** Look at the existing `PublishAsync(T message, PublishOptions options, ...)` implementation to see how `exchangeName`, `routingKey`, and `queueName` are resolved. The private helper mirrors that logic with the addition of `scheduledAt`. Adjust if the signature doesn't match exactly what you see in the file.

**Step 4: Run tests to verify they pass**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ --filter "OutboxPublisher_PublishWithDelay_SetsScheduledAt|OutboxPublisher_PublishWithoutDelay_LeavesScheduledAtNull" -v
```
Expected: PASS.

**Step 5: Run full suite**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ -v
```
Expected: all tests pass.

**Step 6: Commit**

```bash
git add src/Donakunn.MessagingOverQueue/Persistence/OutboxPublisher.cs \
        src/Donakunn.MessagingOverQueue.Test/Unit/Publishing/DelayedPublishingTests.cs
git commit -m "feat: implement delay overloads in OutboxPublisher"
```

---

### Task 4: Update SQL schema — CREATE TABLE and idempotent ALTER TABLE migration

**Files:**
- Modify: `src/Donakunn.MessagingOverQueue/Persistence/Providers/SqlServer/SqlServerMessageStoreProvider.cs`

There are no automated unit tests for DDL changes — correctness is verified by the integration test in Task 6. In this task, make the schema changes carefully and verify them visually.

**Step 1: Add `ScheduledAt` column to `GetCreateTableScript`**

Find `GetCreateTableScript` (around line 611). Inside the `CREATE TABLE` block, add after `[CorrelationId]`:

```sql
[ScheduledAt] DATETIME2 NULL,
```

The column list should end with:
```sql
[CorrelationId] NVARCHAR(100) NULL,
[ScheduledAt]   DATETIME2     NULL,

CONSTRAINT [PK_{tableName}] ...
```

Also add a new index inside the same `BEGIN...END` block, after the existing indexes:

```sql
-- Index for scheduled message queries
CREATE NONCLUSTERED INDEX [IX_{_options.TableName}_ScheduledAt]
ON [{fullTableName}] ([Direction], [Status], [ScheduledAt])
WHERE [ScheduledAt] IS NOT NULL;
```

**Step 2: Add migration method `GetAddScheduledAtColumnScript`**

Add a new private method below `GetCreateTableScript`:

```csharp
private string GetAddScheduledAtColumnScript()
{
    var fullTableName = string.IsNullOrEmpty(_options.Schema)
        ? _options.TableName
        : $"{_options.Schema}].[{_options.TableName}";

    var objectId = string.IsNullOrEmpty(_options.Schema)
        ? $"N'{_options.TableName}'"
        : $"N'[{_options.Schema}].[{_options.TableName}]'";

    return $"""
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID({objectId})
              AND name = 'ScheduledAt'
        )
        BEGIN
            ALTER TABLE [{fullTableName}] ADD [ScheduledAt] DATETIME2 NULL;
        END
        """;
}
```

**Step 3: Call the migration in `EnsureSchemaAsync`**

Find `EnsureSchemaAsync` (around line 509). After the table creation command, add:

```csharp
// Add ScheduledAt column to existing tables (idempotent)
var migrationScript = GetAddScheduledAtColumnScript();
await using var migrationCommand = new SqlCommand(migrationScript, connection);
migrationCommand.CommandTimeout = _options.CommandTimeoutSeconds;
await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
```

**Step 4: Commit**

```bash
git add src/Donakunn.MessagingOverQueue/Persistence/Providers/SqlServer/SqlServerMessageStoreProvider.cs
git commit -m "feat: add ScheduledAt column to SQL schema with idempotent migration"
```

---

### Task 5: Update SQL queries — INSERT, SELECT, OUTPUT, filter, and `MapEntry`

**Files:**
- Modify: `src/Donakunn.MessagingOverQueue/Persistence/Providers/SqlServer/SqlServerMessageStoreProvider.cs`

All changes are in `SqlServerMessageStoreProvider.cs`. Make them one at a time.

**Step 1: Update `InsertEntryAsync` and `TryInsertEntryAsync` INSERT statements**

Both methods have identical INSERT SQL. Add `ScheduledAt` to both column lists and value lists:

```sql
-- Column list (add after CorrelationId):
..., CorrelationId, ScheduledAt

-- Values list (add after @CorrelationId):
..., @CorrelationId, @ScheduledAt
```

**Step 2: Update `GetByIdAsync` SELECT**

Add `ScheduledAt` to the SELECT column list (after `CorrelationId` at position 16 → becomes position 17):

```sql
SELECT Id, Direction, MessageType, Payload, ExchangeName, RoutingKey, QueueName, Headers,
       HandlerType, CreatedAt, ProcessedAt, Status, RetryCount, LastError,
       LockToken, LockExpiresAt, CorrelationId, ScheduledAt
FROM {0}
WHERE Id = @Id AND Direction = @Direction
```

**Step 3: Update both `AcquireOutboxLockAsync` OUTPUT lists and WHERE clauses**

Both overloads have the same structure. For each:

Add `inserted.ScheduledAt` to the OUTPUT column list:
```sql
OUTPUT inserted.Id, ..., inserted.CorrelationId, inserted.ScheduledAt
```

Add the scheduling filter to the WHERE clause:
```sql
WHERE Direction = @OutboxDirection
  AND (Status = @PendingStatus OR (Status = @ProcessingStatus AND LockExpiresAt < @Now))
  AND (ScheduledAt IS NULL OR ScheduledAt <= @Now)
```

**Step 4: Update `AddParameters`**

Add at the end of `AddParameters`, after `@CorrelationId`:

```csharp
command.Parameters.AddWithValue("@ScheduledAt", (object?)entry.ScheduledAt ?? DBNull.Value);
```

**Step 5: Update `MapEntry`**

`MapEntry` reads columns by ordinal. `ScheduledAt` is now at ordinal 17. Add after `CorrelationId` (ordinal 16):

```csharp
ScheduledAt = reader.IsDBNull(17) ? null : reader.GetDateTime(17)
```

**Step 6: Run the full test suite**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ -v
```
Expected: all existing tests pass (SQL queries affected are exercised by existing integration tests).

**Step 7: Commit**

```bash
git add src/Donakunn.MessagingOverQueue/Persistence/Providers/SqlServer/SqlServerMessageStoreProvider.cs
git commit -m "feat: propagate ScheduledAt through all SQL queries and MapEntry"
```

---

### Task 6: Integration test — scheduled message is held then delivered

**Files:**
- Modify: `src/Donakunn.MessagingOverQueue.Test/Integration/RedisStreams/RedisStreamsPersistenceTests.cs`

This test needs real SQL Server and Redis via Testcontainers. The test class already spins up both containers in `InitializeAsync`.

**Step 1: Write the failing test**

Add to `RedisStreamsPersistenceTests`:

```csharp
[Fact]
public async Task DelayedEvent_IsNotConsumedBeforeScheduledTime_ThenConsumedAfter()
{
    // Arrange
    using var host = await BuildHostWithPersistence(persistence => persistence
        .WithOutbox(opts =>
        {
            opts.BatchSize = 10;
            opts.PollingInterval = TimeSpan.FromMilliseconds(200); // fast poll for test
        }));

    await host.StartAsync();

    var publisher = host.Services.GetRequiredService<IEventPublisher>();
    var shortDelay = TimeSpan.FromSeconds(2);

    // Act — publish with delay
    await publisher.PublishAsync(new SimpleTestEvent { Value = "delayed" }, shortDelay);

    // Assert — not received immediately
    await Task.Delay(500);
    Assert.False(TestContext.Received<SimpleTestEvent>(),
        "Message should not be consumed before its scheduled time");

    // Assert — received after delay elapses
    var received = await TestContext.WaitForMessageAsync<SimpleTestEvent>(
        timeout: TimeSpan.FromSeconds(5));
    Assert.NotNull(received);
    Assert.Equal("delayed", received.Value);

    await host.StopAsync();
}
```

> **Note:** `SimpleTestEvent`, `BuildHostWithPersistence`, and `TestContext.WaitForMessageAsync` follow the same patterns used in existing tests in this file. Look at nearby tests to see how they set up the host and assert message receipt. Use the same helper infrastructure — do not create new ones.

**Step 2: Run to verify it fails**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ --filter "DelayedEvent_IsNotConsumedBeforeScheduledTime_ThenConsumedAfter" -v
```
Expected: FAIL (compilation or runtime — implementation not wired yet from previous tasks, or schema not yet applied).

**Step 3: Run to verify it passes after all previous tasks are complete**

Once Tasks 1–5 are done:

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ --filter "DelayedEvent_IsNotConsumedBeforeScheduledTime_ThenConsumedAfter" -v
```
Expected: PASS.

**Step 4: Run full suite**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ -v
```
Expected: all tests pass.

**Step 5: Commit**

```bash
git add src/Donakunn.MessagingOverQueue.Test/Integration/RedisStreams/RedisStreamsPersistenceTests.cs
git commit -m "test: integration test for delayed event hold-then-deliver"
```

---

### Task 7: Final check and cleanup

**Step 1: Run the full suite one last time**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ -v
```
Expected: all tests pass, no warnings about unused parameters.

**Step 2: Verify the README "Dead Code" section**

Open `README.md` and check the "Dead Code & Unused Implementations" section at the bottom. The new delay feature does not affect any item listed there. No README changes needed.

**Step 3: Final commit if any stragglers**

```bash
git status
# Only commit if there are actual changes
git add -p
git commit -m "chore: cleanup after delayed events implementation"
```
