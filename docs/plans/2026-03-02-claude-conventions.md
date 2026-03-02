# CLAUDE.md Conventions & Context Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add project context, coding conventions, and change history files referenced from CLAUDE.md.

**Architecture:** Three new markdown files in `docs/` referenced by a new section in `CLAUDE.md`. No code changes.

**Tech Stack:** Markdown

---

### Task 1: Create docs/CONTEXT.md

**Files:**
- Create: `docs/CONTEXT.md`

**Step 1: Create the file**

```markdown
# Project Context

## Production Environment

This library handles 10,000+ messages in production with horizontal scaling.
Performance, memory management, and long-running stability are critical.

## Core Principles

Code must be: **clean, simple, reliable, robust, secure.**

1. **DRY** — Don't repeat yourself. Extract shared logic, avoid duplication.
2. **YAGNI** — Don't build what isn't needed. No speculative features.
3. **SOLID** — Single responsibility, open/closed, Liskov substitution, interface segregation, dependency inversion.

## Design Priorities (in order)

1. Correctness — never lose or duplicate messages
2. Reliability — graceful degradation under failure
3. Performance — minimize allocations, pool resources, cache lookups
4. Simplicity — straightforward code over clever abstractions
5. Security — validate at boundaries, no injection vectors
```

**Step 2: Commit**

```bash
git add docs/CONTEXT.md
git commit -m "docs: add project context and core principles"
```

---

### Task 2: Create docs/CONVENTIONS.md

**Files:**
- Create: `docs/CONVENTIONS.md`

**Step 1: Create the file**

```markdown
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
```

**Step 2: Commit**

```bash
git add docs/CONVENTIONS.md
git commit -m "docs: add coding conventions"
```

---

### Task 3: Create docs/HISTORY.md

**Files:**
- Create: `docs/HISTORY.md`

**Step 1: Create the file**

```markdown
# Change History

Add a line for every feature or bugfix: date, what changed, and why.

| Date | Change | Reason |
|------|--------|--------|
| 2026-03-02 | Moved idempotency into HandlerInvoker | Fix DI lifetime issue with scoped middleware |
| 2026-03-02 | Added OutboxSignal fan-out | Replace fixed-interval polling to reduce SQL contention |
| 2026-02-25 | Added delayed message delivery | Support scheduling messages for future processing |
| 2026-02-25 | Removed Command flow | Simplify to single message model |
```

**Step 2: Commit**

```bash
git add docs/HISTORY.md
git commit -m "docs: add change history"
```

---

### Task 4: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md` (insert new section between Build Commands and Architecture Overview, around line 24)

**Step 1: Add the reference section**

Insert after the closing ``` of the Build Commands code block (line 23) and before `## Architecture Overview` (line 25):

```markdown
## Project Context & Rules

See [docs/CONTEXT.md](docs/CONTEXT.md) for production context and core principles.
See [docs/CONVENTIONS.md](docs/CONVENTIONS.md) for all coding standards.
See [docs/HISTORY.md](docs/HISTORY.md) for change history.

**When you implement a feature or fix a bug, add a line to `docs/HISTORY.md` with the date, what changed, and why.**
```

**Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: reference context, conventions, and history from CLAUDE.md"
```
