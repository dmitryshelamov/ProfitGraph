# ProfitGraph Architecture

This document holds binding rules: boundaries, mechanisms and code style. Implementation status and the
migration order live in [roadmap.md](roadmap.md); decisions and their reasoning live in [ADRs](adr/).
Keep this file short: a rule belongs here, its history belongs in an ADR.

## Boundaries

The backend is a layered modular monolith. Project dependencies point inward:

```text
Api, Worker -> Infrastructure, Venues.* -> Application -> Domain -> Engine
```

| Project          | Owns                                                                                                   | Must not                                                        |
|------------------|--------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------|
| `Engine`         | Lowering, graph validation and compilation, evaluation, causal dispatch, trading accounting, replay.   | Perform I/O, reference packages beyond pure computation, know about live trading or venues. |
| `Domain`         | Aggregates, value objects, invariants, explicit state transitions, domain events, repository contracts. | Reference infrastructure packages; depends only on `Engine` and `MediatR.Contracts`.       |
| `Application`    | Commands, queries, handlers, validators, ports (including venue ports), boundary contracts.            | Use HTTP types, SQL, or concrete clients.                       |
| `Infrastructure` | Persistence, migrations, background jobs, outbox, encryption, MediatR behaviors.                       | Own trading or bot invariants, or contain user-facing texts.    |
| `Venues.*`       | One trading venue: market data, instrument rules, fees, order gateway, venue bot settings.             | Reference other venue modules or `Infrastructure`.              |
| `Api`, `Worker`  | Composition roots: HTTP boundary and live runtime host.                                                | Contain domain rules or direct persistence access.              |

Architecture tests in `ProfitGraph.Tests.Unit/Architecture` enforce project references, allowed packages of the
inner projects, venue neutrality and the folder conventions below. A new project must be added to those tests and
to this table.

## Folder Conventions

Inside a project, the first folder level is a **feature**, never a technical type. Everything one feature needs in a
layer lives in that feature's folder. Shared base types and cross-cutting mechanisms live in `Common/`.

Features: `Strategies`, `Backtesting`, `Optimization`, `MarketData`, `Bots`, `Accounts`. Add a new feature folder only
for a new product area, and list it here.

```text
Domain/
  Common/                     AggregateRoot, IDomainEvent, DomainException
  Strategies/                 Strategy.cs, StrategyVersion.cs, IStrategyRepository.cs,
                              StrategyCreated.cs (event), StrategyNameTakenException.cs
Application/
  Common/                     Messaging (ICommand, IQuery), Exceptions, shared ports
  Strategies/
    CreateStrategy/           CreateStrategyCommand.cs, CreateStrategyCommandHandler.cs, CreateStrategyCommandValidator.cs
    GetStrategy/              GetStrategyQuery.cs, GetStrategyQueryHandler.cs
    StrategyContract.cs       contracts shared by the feature's use cases
    StrategyConverter.cs      Domain <-> contract mapping
Infrastructure/
  Common/                     Postgres, Messaging (behaviors, outbox), BackgroundJobs, Security
  Strategies/                 StrategyRepository.cs, StrategyQueries.cs, CreateStrategiesTable.cs (migration)
Api/
  Common/                     Errors, Authentication, Realtime
  Strategies/                 StrategiesController.cs, CreateStrategyRequest.cs, StrategyResponse.cs, StrategyHttpConverter.cs
Venues.Bybit/
  MarketData/, Trading/, BybitVenueModule.cs
```

- **Engine** is organized by engine subsystem instead of product feature: `Graphs/`, `Blocks/`, `Lowering/`,
  `Compilation/`, `Evaluation/`, `Dispatch/`, `Accounting/`, `Replay/`, `Metrics/`, `Templates/`.
- **Use cases** get their own folder named after the use case (`CreateStrategy/`). It holds the command or query, its
  handler and its validator; nothing else.
- **Naming:** `*Command`, `*CommandHandler`, `*CommandValidator`, `*Query`, `*QueryHandler`, `*Contract` for
  Application boundary types, `*Converter` for mapping, `*Request` / `*Response` for HTTP types, `*Repository` for
  aggregate persistence, `*Queries` for SQL text. Domain events are past-tense facts without a suffix (`BotDegraded`).
- **Forbidden top-level folders** in Domain, Application, Infrastructure and Api: `Commands`, `Queries`, `Contracts`,
  `Converters`, `Validators`, `Services`, `Entities`, `ValueObjects`, `Repositories`, `Exceptions`, `Events`,
  `Controllers`, `Handlers`, `Models`.
- **Tests** mirror the source: `ProfitGraph.Tests.Unit/<Project>/<Feature>/...`, for example
  `Tests.Unit/Domain/Bots/BotTransitionsTests.cs`. Architecture tests live in `Tests.Unit/Architecture`.
- A namespace equals the folder path: `ProfitGraph.Application.Strategies.CreateStrategy`.

## Backend Mechanisms

- **Use cases.** Every state change is a command, every read is a query (`ICommand`, `IQuery` in
  `Application/Common/Messaging`), dispatched through MediatR 12.5.0. The API and the Worker use the same style;
  worker loops call commands instead of services. See [ADR 0005](adr/0005-mediatr-application-style.md).
- **Pipeline.** Validation (FluentValidation) runs before the handler; commands run inside a database transaction
  unless marked `INonTransactionalCommand` (remote calls, long-running work).
- **Domain state.** Aggregates change state only through methods that enforce their invariants. Consequences of
  transitions (alerts, feed entries, degradation) are domain events delivered through the outbox.
  See [ADR 0004](adr/0004-explicit-live-state-machines.md).
- **Persistence.** PostgreSQL with Dapper and FluentMigrator. Repositories load and save aggregates; they contain no
  business rules. See [ADR 0006](adr/0006-persistence-and-migrations.md).
- **HTTP errors.** Problem Details with a stable `code` and `traceId`; validation failures add `errors`.
- **Logging.** Serilog JSON to the console. Never log secrets, credentials, signatures or authentication headers.

## Numbers and Time

- Use `decimal` for money, prices, quantities, fees and balances, and decimal strings in transport contracts.
  Indicator series may use `double`; accounting and execution values never do.
- Use UTC timestamps. Simulated market time is explicit replay input, separate from `TimeProvider`.
- Backtest results are deterministic for identical inputs.

## Testing

- Unit tests cover Engine, Domain and Application, including every aggregate state transition.
- Integration tests cover HTTP, persistence and hosts against the disposable test PostgreSQL
  (`docker-compose.test.yml`, port 5436).
- Shouldly for assertions, NUnit for attributes and lifecycle; await asynchronous exception assertions.
- Live code is tested with mocked venue clients only.

## Code Style

Style rules come from the tracked `backend/ProfitGraph.sln.DotSettings`; the shared cleanup profile `Cleanup` is
the final check. Details are listed in [AGENTS.md](../AGENTS.md#c-code-style).
