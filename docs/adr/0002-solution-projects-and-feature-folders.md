# ADR 0002: Solution Projects and Feature Folders

Status: Accepted, 2026-10-24.

## Context

In the MVP, the backtest engine lived inside Domain, live code lived in a parallel `Live.*` project family that
duplicated every layer, and code inside layers was grouped by technical type (`Commands/`, `Queries/`,
`Converters/`), scattering one feature across many folders.

## Decision

```text
ProfitGraph.Engine          pure strategy computation: lowering, compilation, evaluation, dispatch, accounting, replay
ProfitGraph.Domain          aggregates (strategies, runs, optimizations, bots, order intents, users, credentials)
ProfitGraph.Application     commands, queries, validators, ports including venue ports
ProfitGraph.Infrastructure  PostgreSQL, migrations, background jobs, outbox, encryption, MediatR behaviors
ProfitGraph.Venues.*        one project per trading venue
ProfitGraph.Api             HTTP composition root
ProfitGraph.Worker          live runtime composition root
```

Dependencies point inward: `Api, Worker -> Infrastructure, Venues.* -> Application -> Domain -> Engine`.
Inside a project, folders are features; shared base types live in `Common/`.

## Consequences

- "Replay does not depend on live code" is structural: Engine cannot reference anything that knows about bots.
- A bot is an ordinary Domain aggregate and its use cases are ordinary commands; no duplicate layer family.
- Architecture tests enforce the reference graph and the allowed packages of Engine and Domain.
