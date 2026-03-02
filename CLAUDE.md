# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build solution
dotnet build

# Run all tests
dotnet test

# Run specific test suite
dotnet test --filter "FullyQualifiedName~RedisStreams"

# Run single test
dotnet test --filter "FullyQualifiedName~Message_Published_And_Consumed_Successfully"

# Pack NuGet packages
dotnet pack ./src/Donakunn.MessagingOverQueue/Donakunn.MessagingOverQueue.csproj -c Release --output ./artifacts
dotnet pack ./src/Donakunn.MessagingOverQueue.RedisStreams/Donakunn.MessagingOverQueue.RedisStreams.csproj -c Release --output ./artifacts
```

## Project Context & Rules

See [docs/CONTEXT.md](docs/CONTEXT.md) for production context and core principles.
See [docs/CONVENTIONS.md](docs/CONVENTIONS.md) for all coding standards.
See [docs/HISTORY.md](docs/HISTORY.md) for change history.

**When you implement a feature or fix a bug, add a line to `docs/HISTORY.md` with the date, what changed, and why.**

## Architecture Overview

This is an async messaging library for .NET 10 with a Redis Streams backend. The key architectural insight is **handler-based topology auto-discovery** - implement `IMessageHandler<T>` and the library creates all messaging infrastructure automatically.

### Core Flow

```
Startup:
  TopologyScanner → discovers IMessageHandler<T> implementations
  HandlerInvokerFactory → creates strongly-typed HandlerInvoker<T> (once per type)
  HandlerInvokerRegistry → caches invokers in ConcurrentDictionary
  TopologyDeclarer → creates streams and consumer groups

Runtime:
  Consumer receives message → Registry.GetInvoker(type) O(1) lookup
  → Creates DI scope → Invokes handler (no reflection) → Acknowledges
```

### Key Design Patterns

**Single Message Model**: All messages implement `IMessage` or extend `MessageBase`. No Event/Command distinction — use `IMessagePublisher` for all publishing.

**Reflection-Free Handler Dispatch**: `HandlerInvoker<T>` instances are created once at startup and cached. Runtime dispatch is dictionary lookup + direct method call - zero per-message reflection.

**Scoped Handler Lifetime**: Handlers are registered as Scoped in DI. Each message gets fresh handler instance with isolated DbContext and dependencies.

**Provider-Based Persistence**: Outbox pattern uses `IMessageStoreProvider` abstraction with ADO.NET (not EF Core). SQL Server provider built-in, others pluggable.

**Event-Level Versioning**: Stream versioning via `[EventTopology(Version = "v1")]` on message types. No consumer-level version override.

### Project Structure

```
src/
├── Donakunn.MessagingOverQueue/              # Core library
│   ├── Abstractions/                         # IMessage, IMessageHandler<T>, IMessagePublisher
│   ├── Topology/                             # Scanner, Registry, Declarer, Attributes
│   ├── Consuming/Handlers/                   # HandlerInvokerRegistry, HandlerInvokerFactory
│   ├── Persistence/Providers/                # IMessageStoreProvider, SqlServer implementation
│   └── DependencyInjection/                  # ServiceCollectionExtensions, IMessagingBuilder
├── Donakunn.MessagingOverQueue.RedisStreams/ # Redis Streams provider
└── Donakunn.MessagingOverQueue.Test/         # Tests with Testcontainers
    ├── Unit/                                 # Handler, middleware, topology tests
    └── Integration/                          # End-to-end with real containers
        ├── TestDoubles/                      # Reusable test messages/handlers
        └── Infrastructure/                   # TestExecutionContext for test isolation
```

### Key Files

| Path | Purpose |
|------|---------|
| `Abstractions/Messages/IMessage.cs` | Message interface - all messages implement this |
| `Abstractions/Publishing/IMessagePublisher.cs` | Unified publish interface with auto-routing and delayed support |
| `Abstractions/Consuming/IMessageHandler.cs` | Handler interface - implement this |
| `Consuming/Handlers/HandlerInvokerRegistry.cs` | O(1) handler lookup cache |
| `Topology/Abstractions/ITopologyScanner.cs` | Handler discovery interface |
| `Topology/Attributes/ConsumerQueueAttribute.cs` | Handler queue/concurrency config |
| `Persistence/Providers/IMessageStoreProvider.cs` | Outbox database abstraction |

### Testing

Tests use **Testcontainers** for real Redis/SQL Server instances. The `TestExecutionContext` pattern provides isolation for parallel test execution - each test creates isolated handler state.

Test doubles are in `Integration/TestDoubles/TestMessagesAndHandlers.cs`.
