# Versioning Binding Tests Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add 6 integration tests to `RedisStreamsTopologyTests` covering all versioning binding scenarios for `[EventTopology(Version)]` and `[ConsumerTopology(Version)]`.

**Architecture:** All new types and tests live in `RedisStreamsTopologyTests.cs`. Three event types and three handler types are appended to the existing `#region Additional Test Handlers` block. Tests are split into structural (topology creation in Redis) and behavioral (message routing and isolation). No new files, no changes to shared infrastructure.

**Tech Stack:** .NET 10, xUnit 2.9, StackExchange.Redis, Testcontainers.Redis, `RedisStreamsIntegrationTestBase` infrastructure.

---

## Context you need before starting

- Design doc: `docs/plans/2026-02-27-versioning-binding-tests-design.md`
- Test file: `src/Donakunn.MessagingOverQueue.Test/Integration/RedisStreams/RedisStreamsTopologyTests.cs`
- Base class: `src/Donakunn.MessagingOverQueue.Test/Integration/RedisStreams/Infrastructure/RedisStreamsIntegrationTestBase.cs`
  - `BuildHost<THandlerMarker>()` scans the whole test assembly — all handlers get registered regardless of type marker.
  - Helper methods: `ConsumerGroupExistsAsync(streamKey, groupName)`, `GetStreamLengthAsync(streamKey)`
  - `StreamPrefix` property: per-test unique prefix applied by `BuildStreamKey` as `{prefix}:{streamName}`
- Naming logic (already unit-tested):
  - `[EventTopology(Category="versioning", Name="order-placed", Version="v1")]` → stream `versioning.order-placed.v1`
  - `[ConsumerTopology(Version="v2")]` on a handler → stream key uses `v2`, overriding the event's `v1`
  - Handler with no `[ConsumerTopology]` → falls back to event version
  - Consumer group name never includes version: `{service}.{name}` (e.g. `test-service.order-placed`)
- Run tests with: `dotnet test src/Donakunn.MessagingOverQueue.Test/ --filter "FullyQualifiedName~RedisStreamsTopologyTests" -v`
- Run full suite: `dotnet test src/Donakunn.MessagingOverQueue.Test/ -v`

---

### Task 1: Add versioned test types and required usings

**Files:**
- Modify: `src/Donakunn.MessagingOverQueue.Test/Integration/RedisStreams/RedisStreamsTopologyTests.cs`

**Step 1: Add two missing `using` directives**

At the top of `RedisStreamsTopologyTests.cs`, after the existing usings, add:

```csharp
using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Topology.Attributes;
```

**Step 2: Add three versioned events**

Inside the `#region Additional Test Handlers` block, after the existing `MultiHandlerEventHandler2` class, add:

```csharp
// ── Versioning binding test types ──────────────────────────────────────────

/// <summary>Event with explicit version v1.</summary>
[EventTopology(Category = "versioning", Name = "order-placed", Version = "v1")]
public record VersionedV1Event : Event
{
    public string Value { get; set; } = string.Empty;
}

/// <summary>Event with explicit version v2 (separate stream from v1).</summary>
[EventTopology(Category = "versioning", Name = "order-placed", Version = "v2")]
public record VersionedV2Event : Event
{
    public string Value { get; set; } = string.Empty;
}

/// <summary>Event with no version attribute.</summary>
[EventTopology(Category = "versioning", Name = "unversioned-order")]
public record UnversionedEvent : Event
{
    public string Value { get; set; } = string.Empty;
}
```

**Step 3: Add three versioned handlers**

Immediately after the three events, add:

```csharp
/// <summary>
/// No [ConsumerTopology] — falls back to event version.
/// Binds to stream: versioning.order-placed.v1
/// Consumer group: test-service.order-placed
/// </summary>
public class VersionedV1Handler : IMessageHandler<VersionedV1Event>
{
    private const string HandlerKey = nameof(VersionedV1Handler);

    public static int HandleCount =>
        TestExecutionContextAccessor.GetRequired().GetCounter(HandlerKey).Count;

    public static void Reset() =>
        TestExecutionContextAccessor.GetRequired().GetCounter(HandlerKey).Reset();

    public static Task WaitForCountAsync(int expected, TimeSpan timeout) =>
        TestExecutionContextAccessor.GetRequired().GetCounter(HandlerKey).WaitForCountAsync(expected, timeout);

    public Task HandleAsync(VersionedV1Event message, IMessageContext context, CancellationToken cancellationToken)
    {
        TestExecutionContextAccessor.GetRequired().GetCounter(HandlerKey).Increment();
        return Task.CompletedTask;
    }
}

/// <summary>
/// [ConsumerTopology(Version="v2")] overrides the event's v1.
/// Binds to stream: versioning.order-placed.v2
/// Consumer group: test-service.order-placed
/// </summary>
[ConsumerTopology(Version = "v2")]
public class ConsumerOverridesVersionHandler : IMessageHandler<VersionedV1Event>
{
    private const string HandlerKey = nameof(ConsumerOverridesVersionHandler);

    public static int HandleCount =>
        TestExecutionContextAccessor.GetRequired().GetCounter(HandlerKey).Count;

    public static void Reset() =>
        TestExecutionContextAccessor.GetRequired().GetCounter(HandlerKey).Reset();

    public static Task WaitForCountAsync(int expected, TimeSpan timeout) =>
        TestExecutionContextAccessor.GetRequired().GetCounter(HandlerKey).WaitForCountAsync(expected, timeout);

    public Task HandleAsync(VersionedV1Event message, IMessageContext context, CancellationToken cancellationToken)
    {
        TestExecutionContextAccessor.GetRequired().GetCounter(HandlerKey).Increment();
        return Task.CompletedTask;
    }
}

/// <summary>
/// [ConsumerTopology(Version="v1")] adds a version to an event that has none.
/// Binds to stream: versioning.unversioned-order.v1
/// Consumer group: test-service.unversioned-order
/// </summary>
[ConsumerTopology(Version = "v1")]
public class ConsumerAddsVersionHandler : IMessageHandler<UnversionedEvent>
{
    private const string HandlerKey = nameof(ConsumerAddsVersionHandler);

    public static int HandleCount =>
        TestExecutionContextAccessor.GetRequired().GetCounter(HandlerKey).Count;

    public static void Reset() =>
        TestExecutionContextAccessor.GetRequired().GetCounter(HandlerKey).Reset();

    public static Task WaitForCountAsync(int expected, TimeSpan timeout) =>
        TestExecutionContextAccessor.GetRequired().GetCounter(HandlerKey).WaitForCountAsync(expected, timeout);

    public Task HandleAsync(UnversionedEvent message, IMessageContext context, CancellationToken cancellationToken)
    {
        TestExecutionContextAccessor.GetRequired().GetCounter(HandlerKey).Increment();
        return Task.CompletedTask;
    }
}
```

**Step 4: Run full suite to verify nothing broke**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ -v
```

Expected: all previously passing tests still pass, no compile errors.

**Step 5: Commit**

```bash
git add src/Donakunn.MessagingOverQueue.Test/Integration/RedisStreams/RedisStreamsTopologyTests.cs
git commit -m "test: add versioned test types for versioning binding tests"
```

---

### Task 2: Add structural versioning topology tests

Structural tests verify that the correct Redis streams and consumer groups are created during topology declaration for each versioning scenario.

**Files:**
- Modify: `src/Donakunn.MessagingOverQueue.Test/Integration/RedisStreams/RedisStreamsTopologyTests.cs`

**Step 1: Write the three structural tests**

Add these three tests to `RedisStreamsTopologyTests` class body, before the `#region Additional Test Handlers` block:

```csharp
[Fact]
public async Task Versioned_Event_Creates_Versioned_Stream_Key()
{
    // Arrange
    var streamKey = $"{StreamPrefix}:versioning.order-placed.v1";
    var consumerGroup = "test-service.order-placed";

    // Act
    using var host = await BuildHost<VersionedV1Handler>();
    await Task.Delay(1000);

    // Assert
    var groupExists = await ConsumerGroupExistsAsync(streamKey, consumerGroup);
    Assert.True(groupExists,
        $"Consumer group '{consumerGroup}' should be created on versioned stream '{streamKey}'");
}

[Fact]
public async Task Consumer_Version_Override_Binds_To_Different_Stream()
{
    // Arrange — event is v1, handler overrides to v2
    var v2StreamKey = $"{StreamPrefix}:versioning.order-placed.v2";
    var consumerGroup = "test-service.order-placed";

    // Act
    using var host = await BuildHost<ConsumerOverridesVersionHandler>();
    await Task.Delay(1000);

    // Assert — group exists on the overridden v2 stream
    var groupOnV2 = await ConsumerGroupExistsAsync(v2StreamKey, consumerGroup);
    Assert.True(groupOnV2,
        $"[ConsumerTopology(Version=\"v2\")] should bind consumer group to v2 stream, not the event's v1");
}

[Fact]
public async Task Consumer_Adds_Version_To_Unversioned_Event()
{
    // Arrange — event has no version; handler declares v1
    var streamKey = $"{StreamPrefix}:versioning.unversioned-order.v1";
    var consumerGroup = "test-service.unversioned-order";

    // Act
    using var host = await BuildHost<ConsumerAddsVersionHandler>();
    await Task.Delay(1000);

    // Assert
    var groupExists = await ConsumerGroupExistsAsync(streamKey, consumerGroup);
    Assert.True(groupExists,
        $"[ConsumerTopology(Version=\"v1\")] should add version suffix to unversioned event's stream key");
}
```

**Step 2: Run the three new tests**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ \
  --filter "FullyQualifiedName~Versioned_Event_Creates_Versioned_Stream_Key|FullyQualifiedName~Consumer_Version_Override_Binds_To_Different_Stream|FullyQualifiedName~Consumer_Adds_Version_To_Unversioned_Event" \
  -v
```

Expected: all three PASS.

**Step 3: Run full suite**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ -v
```

Expected: all tests pass.

**Step 4: Commit**

```bash
git add src/Donakunn.MessagingOverQueue.Test/Integration/RedisStreams/RedisStreamsTopologyTests.cs
git commit -m "test: add structural versioning topology tests"
```

---

### Task 3: Add behavioral versioning routing tests

Behavioral tests verify that messages are routed to the correct versioned stream, that handlers receive from their bound stream, and that version-pinned consumers are isolated from messages on other version streams.

**Files:**
- Modify: `src/Donakunn.MessagingOverQueue.Test/Integration/RedisStreams/RedisStreamsTopologyTests.cs`

**Step 1: Write the three behavioral tests**

Add after the three structural tests from Task 2:

```csharp
[Fact]
public async Task Handler_Without_Version_Override_Binds_To_Event_Version()
{
    // A handler with no [ConsumerTopology] attribute receives messages
    // published to the event's own versioned stream.
    VersionedV1Handler.Reset();

    using var host = await BuildHost<VersionedV1Handler>();
    var publisher = host.Services.GetRequiredService<IEventPublisher>();

    // Act
    await publisher.PublishAsync(new VersionedV1Event { Value = "versioned" });
    await VersionedV1Handler.WaitForCountAsync(1, DefaultTimeout);

    // Assert
    Assert.Equal(1, VersionedV1Handler.HandleCount);
}

[Fact]
public async Task Publisher_Routes_To_Versioned_Stream()
{
    // Publishing a versioned event writes the message to the versioned stream key,
    // not a plain unversioned key.
    var streamKey = $"{StreamPrefix}:versioning.order-placed.v1";

    using var host = await BuildHost<VersionedV1Handler>();
    var publisher = host.Services.GetRequiredService<IEventPublisher>();

    // Act
    await publisher.PublishAsync(new VersionedV1Event { Value = "routing-test" });
    await Task.Delay(500);

    // Assert
    var length = await GetStreamLengthAsync(streamKey);
    Assert.True(length > 0,
        $"Published VersionedV1Event should appear in versioned stream '{streamKey}'");
}

[Fact]
public async Task Two_Version_Overrides_Create_Independent_Streams()
{
    // VersionedV1Handler subscribes to versioning.order-placed.v1
    // ConsumerOverridesVersionHandler subscribes to versioning.order-placed.v2
    // Publishing VersionedV1Event (goes to v1) should reach only the v1 handler.
    VersionedV1Handler.Reset();
    ConsumerOverridesVersionHandler.Reset();

    using var host = await BuildHost<VersionedV1Handler>();
    var publisher = host.Services.GetRequiredService<IEventPublisher>();

    // Act — publish to v1 stream
    await publisher.PublishAsync(new VersionedV1Event { Value = "v1-message" });
    await VersionedV1Handler.WaitForCountAsync(1, DefaultTimeout);

    // Brief extra wait to confirm the v2-bound handler did not receive it
    await Task.Delay(500);

    // Assert
    Assert.Equal(1, VersionedV1Handler.HandleCount);
    Assert.Equal(0, ConsumerOverridesVersionHandler.HandleCount);
}
```

**Step 2: Run the three new behavioral tests**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ \
  --filter "FullyQualifiedName~Handler_Without_Version_Override_Binds_To_Event_Version|FullyQualifiedName~Publisher_Routes_To_Versioned_Stream|FullyQualifiedName~Two_Version_Overrides_Create_Independent_Streams" \
  -v
```

Expected: all three PASS.

**Step 3: Run full suite**

```bash
dotnet test src/Donakunn.MessagingOverQueue.Test/ -v
```

Expected: all tests pass, including all 6 new versioning tests.

**Step 4: Commit**

```bash
git add src/Donakunn.MessagingOverQueue.Test/Integration/RedisStreams/RedisStreamsTopologyTests.cs
git commit -m "test: add behavioral versioning routing and isolation tests"
```
