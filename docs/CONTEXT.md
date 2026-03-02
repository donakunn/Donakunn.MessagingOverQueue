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
