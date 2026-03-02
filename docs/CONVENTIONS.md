# Coding Conventions

## Naming

- Classes, interfaces, methods, properties, enums: PascalCase
- Interfaces: I-prefix (`IMessage`, `IMessageHandler<T>`)
- Private fields: _camelCase (`_logger`, `_options`)
- Parameters and locals: camelCase
- Constants: PascalCase (`SectionName`)
- Type suffixes: Middleware, Repository, Handler, Provider, Options, Builder

## File Organization

- One class per file (interface + sealed implementation together is OK)
- Folders match namespace hierarchy
- Abstractions in `Abstractions/` subfolder

## Code Style

- 4-space indentation
- Explicit access modifiers on all members
- Sealed classes for internal implementations
- Records for immutable data types, classes for mutable behavior
- Expression-bodied members for simple getters and methods
- Nullable reference types enabled — use `T?` and `ArgumentNullException.ThrowIfNull()`

## Async Patterns

- CancellationToken always last parameter with `= default`
- `.ConfigureAwait(false)` in library code
- `ValueTask` for high-frequency middleware, `Task` for public API
- Check `IAsyncDisposable` before disposing async resources

## Dependency Injection

- Scoped: handlers (per-message lifetime)
- Singleton: registries, factories, serializers
- HostedService: background workers
- `TryAdd` for overridable defaults
- Builder pattern with fluent API (`Add`/`With`/`Use` prefixes)
- Options pattern: class ends with `Options`, static `SectionName` constant

## Error Handling

- Guard clauses at method entry: `ArgumentNullException.ThrowIfNull()`
- Framework exception types preferred over custom
- Exception filtering with `when` clauses
- Never swallow `OperationCanceledException`

## Logging

- `ILogger<T>` via constructor injection
- Structured placeholders: `"Message {MessageId} processed by {HandlerType}"`
- Debug for operational details, Information for key events, Warning for concerns, Error for failures

## XML Documentation

- Required on all public types and members
- `<summary>` on everything, `<remarks>` for complex contracts
- `<example>` blocks on interfaces showing usage
- `<inheritdoc />` on implementations that defer to interface docs

## Testing

- xUnit with `[Fact]`/`[Theory]`, Moq for mocking
- Testcontainers for real Redis and SQL Server
- Test isolation via `TestExecutionContext` and unique stream prefixes per test
- Test doubles in `Integration/TestDoubles/`
- Base classes: `RedisStreamsIntegrationTestBase`, `LoadTestBase`
