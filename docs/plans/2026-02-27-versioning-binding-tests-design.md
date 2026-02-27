# Versioning Binding Tests Design

**Date:** 2026-02-27
**Status:** Approved
**Branch:** attribute-enhance

## Problem

`RedisStreamsTopologyTests` has no coverage for versioning binding. The unit tests in `DefaultTopologyNamingConventionTests` verify naming logic, but nothing verifies that the actual Redis topology (stream keys, consumer groups) is created correctly when `[EventTopology(Version)]` and `[ConsumerTopology(Version)]` are used, or that messages route to the right versioned stream.

## Goals

- Cover all versioning binding scenarios end-to-end in Redis.
- Keep test types co-located with the topology test file (Option A).
- No changes to shared infrastructure or other test files.

## What Does Not Change

- `DefaultTopologyNamingConventionTests` — naming logic already covered there.
- `TestMessagesAndHandlers.cs` — no new shared types needed.
- All existing topology tests — unaffected.

## Design

### New Test Types

Appended to the `#region Additional Test Handlers` block in `RedisStreamsTopologyTests.cs`.

**Events:**

```csharp
[EventTopology(Category = "versioning", Name = "order-placed", Version = "v1")]
public record VersionedV1Event : Event { }

[EventTopology(Category = "versioning", Name = "order-placed", Version = "v2")]
public record VersionedV2Event : Event { }

[EventTopology(Category = "versioning", Name = "unversioned-order")]
public record UnversionedEvent : Event { }
```

**Handlers** (all follow the standard counter/reset/WaitFor pattern):

```csharp
// Subscribes to v1 stream — no ConsumerTopology, falls back to event version
public class VersionedV1Handler : IMessageHandler<VersionedV1Event> { ... }

// Consumer version overrides event version — subscribes to v2 stream
[ConsumerTopology(Version = "v2")]
public class ConsumerOverridesVersionHandler : IMessageHandler<VersionedV1Event> { ... }

// Consumer adds version to an unversioned event — subscribes to v1 stream
[ConsumerTopology(Version = "v1")]
public class ConsumerAddsVersionHandler : IMessageHandler<UnversionedEvent> { ... }
```

### New Tests

| Test | Assertion |
|---|---|
| `Versioned_Event_Creates_Versioned_Stream_Key` | Stream `{prefix}:versioning.order-placed.v1` exists; group `test-service.order-placed` exists on it |
| `Handler_Without_Version_Override_Binds_To_Event_Version` | Same stream and group as above — no `[ConsumerTopology]` means handler lands on event's versioned stream |
| `Consumer_Version_Override_Binds_To_Different_Stream` | Group exists on `versioning.order-placed.v2`; group does NOT exist on `versioning.order-placed.v1` |
| `Publisher_Routes_To_Versioned_Stream` | After publishing `VersionedV1Event`, stream length > 0 on `versioning.order-placed.v1` |
| `Two_Version_Overrides_Create_Independent_Streams` | Publish `VersionedV1Event`; `VersionedV1Handler` receives it; `ConsumerOverridesVersionHandler` does not |
| `Consumer_Adds_Version_To_Unversioned_Event` | Group exists on `versioning.unversioned-order.v1` |

### Expected Stream Keys (with prefix)

```
{prefix}:versioning.order-placed.v1   ← VersionedV1Event / VersionedV1Handler / ConsumerOverridesVersionHandler default
{prefix}:versioning.order-placed.v2   ← ConsumerOverridesVersionHandler (version override)
{prefix}:versioning.unversioned-order.v1   ← ConsumerAddsVersionHandler (consumer adds version)
```

### Consumer Group Names

Consumer groups do not include the version (follows `{service}.{name}` convention):

```
test-service.order-placed          ← for both VersionedV1Handler and ConsumerOverridesVersionHandler
test-service.unversioned-order     ← for ConsumerAddsVersionHandler
```

## Files Changed

```
src/Donakunn.MessagingOverQueue.Test/Integration/RedisStreams/RedisStreamsTopologyTests.cs
  ← 6 new test methods
  ← 6 new test types in #region Additional Test Handlers
```
