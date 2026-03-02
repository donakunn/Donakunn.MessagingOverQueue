# Change History

Add a line for every feature or bugfix: date, what changed, and why.

| Date | Change | Reason |
|------|--------|--------|
| 2026-03-02 | Moved idempotency into HandlerInvoker | Fix DI lifetime issue with scoped middleware |
| 2026-03-02 | Added OutboxSignal fan-out | Replace fixed-interval polling to reduce SQL contention |
| 2026-02-25 | Added delayed message delivery | Support scheduling messages for future processing |
| 2026-02-25 | Removed Command flow | Simplify to single message model |
