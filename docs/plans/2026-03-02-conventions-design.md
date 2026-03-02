# CLAUDE.md Conventions & Context Design

## Problem

CLAUDE.md lacks coding convention rules, production context, and change history. Without these, Claude Code may introduce inconsistent patterns, ignore performance constraints, or lose track of why changes were made.

## Solution

Create 3 reference files and update CLAUDE.md to point to them:

1. **`docs/CONTEXT.md`** — Production environment (10k+ messages, horizontal scaling), core principles (clean, simple, reliable, robust, secure), DRY/YAGNI/SOLID, design priorities
2. **`docs/CONVENTIONS.md`** — All coding standards: naming, file organization, code style, async patterns, DI, error handling, logging, XML docs, testing
3. **`docs/HISTORY.md`** — Running changelog (date, what, why) updated on every feature/fix

## CLAUDE.md Changes

Add a new section between Build Commands and Architecture Overview:

```markdown
## Project Context & Rules

See [docs/CONTEXT.md](docs/CONTEXT.md) for production context and core principles.
See [docs/CONVENTIONS.md](docs/CONVENTIONS.md) for all coding standards.
See [docs/HISTORY.md](docs/HISTORY.md) for change history.

**When you implement a feature or fix a bug, add a line to `docs/HISTORY.md` with the date, what changed, and why.**
```

## Status

APPROVED
