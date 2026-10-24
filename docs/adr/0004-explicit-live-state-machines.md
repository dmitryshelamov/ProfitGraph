# ADR 0004: Explicit Live State Machines

Status: Accepted, 2026-10-24.

## Context

In the MVP, the live domain had about 500 lines while live infrastructure had 26,000. Bot and order-intent state
transitions, degradation on insufficient balance, alert creation and user-facing texts were implemented inside SQL
methods of a 1,200-line store, so the rules could only be tested through PostgreSQL.

## Decision

- `Bot` and `OrderIntent` are Domain aggregates whose transitions are methods enforcing invariants, for example
  `Start`, `Stop`, `Degrade(reason)`, `RecordFailure`, `ScheduleRestart`, and `Claim`, `MarkSent`, `Reject`, `Fail`.
- Bot construction takes only required data; restoration from storage uses a separate factory.
- Consequences of transitions (alerts, bot event feed entries) are domain events delivered through the outbox.
- Repositories load and save aggregates with optimistic concurrency and the lease fencing epoch; they contain no
  business rules.
- User-facing texts are produced from reason codes at the presentation or notification boundary, not in storage.

## Consequences

- Every transition is covered by Domain unit tests; integration tests verify persistence and fencing only.
- The lease, fencing, checkpoint and outbox mechanisms of the MVP are kept; only the placement of rules changes.

## Amendment: row locks for live aggregates (stage 5.3)

Bots, bot leases and order intents are written under a row lock taken when they are loaded in the writing
transaction (ADR 0016) instead of an optimistic version number. Their writers are short transactions that already lock
the lease for fencing, and a lock cannot fail late the way a version conflict does after a venue call.
