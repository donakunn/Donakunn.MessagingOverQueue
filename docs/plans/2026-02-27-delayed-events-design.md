# Delayed Events Design

**Date:** 2026-02-27
**Status:** Approved
**Branch:** attribute-enhance

## Problem

There is no way to publish a message that should only become visible to consumers after a delay. Use cases: order reminders, payment retries, deferred workflow steps.

## Goals

- Publisher says "deliver this message after X delay" — message is invisible to consumers until the scheduled time.
- Simple API: `TimeSpan` overload on the existing `IEventPublisher` / `ICommandSender`.
- No new background services, no new storage abstractions.
- Requires SQL Server outbox persistence. Fails fast with a clear error if not configured.
- At-least-once delivery, consistent with outbox guarantees.

## What Does Not Change

- `OutboxProcessor` — no changes. The filter addition in `AcquireOutboxLockAsync` is enough.
- `RedisStreamsMessagePublisher` — inherits default `NotSupportedException` from interface; no changes needed.
- All existing immediate-publish paths — `ScheduledAt IS NULL` rows are unaffected by the new filter.

## Design

### API

Add a `TimeSpan delay` overload to `IEventPublisher` and `ICommandSender` using default interface implementations:

```csharp
// IEventPublisher
Task PublishAsync<T>(T @event, TimeSpan delay, CancellationToken cancellationToken = default)
    where T : IEvent
    => throw new NotSupportedException("Delayed publishing requires outbox persistence.");

// ICommandSender
Task SendAsync<T>(T command, TimeSpan delay, CancellationToken cancellationToken = default)
    where T : ICommand
    => throw new NotSupportedException("Delayed publishing requires outbox persistence.");
```

`OutboxPublisher` overrides both with real implementations that set `ScheduledAt`. No new interfaces or services are introduced.

**Usage:**
```csharp
// inject IEventPublisher as usual — same interface, new overload
await publisher.PublishAsync(new OrderReminderEvent { OrderId = id }, TimeSpan.FromMinutes(10));
```

### Data Model

Add `ScheduledAt DateTime?` to `MessageStoreEntry`:

```csharp
/// <summary>
/// When the message should become visible for processing (outbox only).
/// Null means immediately eligible.
/// </summary>
public DateTime? ScheduledAt { get; set; }
```

Update `CreateOutboxEntry` factory to accept an optional `scheduledAt` parameter.

### Outbox Filter

Add a single condition to both `AcquireOutboxLockAsync` overloads in `SqlServerMessageStoreProvider`:

```sql
-- before
AND (Status = @PendingStatus OR (Status = @ProcessingStatus AND LockExpiresAt < @Now))

-- after
AND (Status = @PendingStatus OR (Status = @ProcessingStatus AND LockExpiresAt < @Now))
AND (ScheduledAt IS NULL OR ScheduledAt <= @Now)
```

Messages with a future `ScheduledAt` remain invisible until the outbox processor's next poll after their scheduled time elapses. Precision = `OutboxOptions.PollingInterval` (default 5s).

### Schema Changes

**New CREATE TABLE** — add column and index directly to `GetCreateTableScript`:

```sql
[ScheduledAt] DATETIME2 NULL,
```

```sql
CREATE NONCLUSTERED INDEX [IX_{tableName}_ScheduledAt]
ON [{tableName}] ([Direction], [Status], [ScheduledAt])
WHERE [ScheduledAt] IS NOT NULL;
```

**Migration for existing tables** — idempotent `ALTER TABLE`, called at startup:

```sql
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[{schema}].[{tableName}]')
      AND name = 'ScheduledAt'
)
ALTER TABLE [{schema}].[{tableName}] ADD [ScheduledAt] DATETIME2 NULL;
```

The migration guard runs inside the existing `EnsureCreatedAsync` flow so no separate startup call is needed.

### SQL Provider Changes

All changes are in `SqlServerMessageStoreProvider`:

| Method | Change |
|---|---|
| `InsertEntryAsync` / `TryInsertEntryAsync` | Add `ScheduledAt` to INSERT column list and parameters |
| `GetByIdAsync` | Add `ScheduledAt` to SELECT column list |
| `AcquireOutboxLockAsync` (both overloads) | Add `ScheduledAt` to OUTPUT list + `AND (ScheduledAt IS NULL OR ScheduledAt <= @Now)` to WHERE |
| `AddParameters` | Add `@ScheduledAt` parameter (`DBNull.Value` when null) |
| `MapEntry` | Read `ScheduledAt` at ordinal 17 |
| `GetCreateTableScript` | Add `[ScheduledAt] DATETIME2 NULL` column + index |
| `EnsureCreatedAsync` | Call new `GetAddScheduledAtColumnScript` after table creation |

## Data Flow

```
PublishAsync(event, delay: TimeSpan.FromMinutes(10))
  → OutboxPublisher.PublishAsync(event, delay)
  → CreateOutboxEntry(..., scheduledAt: DateTime.UtcNow + delay)
  → IOutboxRepository.AddAsync(entry)   ← identical to immediate publish

OutboxProcessor.AcquireOutboxLockAsync
  → SQL: WHERE ... AND (ScheduledAt IS NULL OR ScheduledAt <= @Now)
  → scheduled rows invisible until time elapses
  → when due: locked → published to Redis Stream → marked Published
```

## Delivery Guarantees

- **At-least-once** — identical to standard outbox. Message is locked before publish, marked Published after. A crash between the two replays on the next poll.
- **Idempotency** — consumer-side idempotency middleware deduplicates replays using message `Id`.
- **Precision** — fires within `scheduledAt + OutboxOptions.PollingInterval` (default +5s).
- **Prerequisite** — `IEventPublisher.PublishAsync(event, delay)` throws `NotSupportedException` if outbox persistence is not configured (i.e., `RedisStreamsMessagePublisher` handles the call).

## Files Changed

```
src/Donakunn.MessagingOverQueue/
  Abstractions/Publishing/IEventPublisher.cs          ← new delay overloads (default impl)
  Persistence/Entities/MessageStoreEntry.cs           ← ScheduledAt field + factory param
  Persistence/OutboxPublisher.cs                      ← implement delay overloads
  Persistence/Providers/SqlServer/
    SqlServerMessageStoreProvider.cs                  ← schema, filter, mapping, migration
```
