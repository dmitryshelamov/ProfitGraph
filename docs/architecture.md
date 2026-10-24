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
| `Engine`         | Lowering, graph validation and compilation, evaluation, causal dispatch, trading accounting, replay.   | Perform I/O, reference packages beyond pure computation, know about live trading or venues (ADR 0009). |
| `Domain`         | Aggregates, value objects, invariants, explicit state transitions, domain events, repository contracts. | Reference infrastructure packages; depends only on `Engine` and `MediatR.Contracts`.       |
| `Application`    | Commands, queries, handlers, validators, ports (including venue ports), boundary contracts.            | Use HTTP types, SQL, or concrete clients.                       |
| `Infrastructure` | Persistence, migrations, background jobs, outbox, encryption, MediatR behaviors.                       | Own trading or bot invariants, or contain user-facing texts.    |
| `Venues.*`       | One trading venue: market data, instrument rules, fees, order gateway, venue bot settings.             | Reference other venue modules or `Infrastructure`.              |
| `Api`, `Worker`  | Composition roots: HTTP boundary and live runtime host.                                                | Contain domain rules or direct persistence access.              |

Architecture tests in `ProfitGraph.Tests.Unit/Architecture` enforce project references, allowed packages of the
inner projects, venue neutrality, the folder conventions below, one type per file, and migration and repository
naming. A new project must be added to those tests and
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
  Common/                     Messaging (ICommand, IQuery), Exceptions, Transactions
  Strategies/
    CreateStrategy/           CreateStrategyCommand.cs, CreateStrategyCommandHandler.cs, CreateStrategyCommandValidator.cs
    GetStrategy/              GetStrategyQuery.cs, GetStrategyQueryHandler.cs
    Contracts/                StrategyInternal.cs (types returned by and passed to use cases)
    Converters/               StrategyInternalConverter.cs (ToInternal / ToDomain extensions)
Infrastructure/
  Common/
    Postgres/Migrations/      every migration, e.g. 202610240001_create_user_table.cs (class CreateUserTableMigration)
    Postgres/, Messaging/     connection, transactions, behaviors
  Accounts/
    Repositories/             UserRepository.cs
      Contracts/              UserDb.cs (row model: class with init properties)
      Converters/             UserDbConverter.cs (ToDomain / ToDb extensions)
      Queries/                UserQueries.cs (SQL text)
    Security/                 IdentityPasswordHasher.cs
Api/
  Common/                     Errors, Authentication, Realtime
  Strategies/                 StrategiesController.cs
    Contracts/                CreateStrategyRequest.cs, StrategyResponse.cs
    Converters/               StrategyResponseConverter.cs (ToResponse extensions)
Venues.Bybit/
  MarketData/, Trading/, BybitVenueModule.cs
```

- **Engine** is organized by engine subsystem instead of product feature: `Common/`, `Market/`, `Accounting/`,
  `Graphs/` (with `Validation/`, `Dependencies/`, `Composites/`), `Nodes/` (node types, `BuiltIn/`), `Blocks/`,
  `Lowering/`, `Compilation/`, `Evaluation/`, `Dispatch/`, `Replay/`, `Metrics/`, `Templates/`.
  Engine exceptions derive from `Common/EngineException`.
- **Use cases** get their own folder named after the use case (`CreateStrategy/`). It holds the command or query, its
  handler and its validator; nothing else.
- **Naming:** `*Command`, `*CommandHandler`, `*CommandValidator`, `*Query`, `*QueryHandler`, `*Internal` for
  Application contracts, `*InternalConverter` / `*ResponseConverter` / `*DbConverter` for mapping, `*Request` / `*Response` for HTTP types, `*Repository` for
  aggregate persistence, `*Db` for repository row models, `*DbConverter` for row mapping, `*Queries` for SQL text,
  `*Migration` for migrations. Domain events are past-tense facts without a suffix (`BotDegraded`).
- **One type per file**, named after the type, with no nested types. Generic and non-generic variants of one name
  are separate files (`ICommand.cs`, `ICommandOfTResponse.cs`).
- **Migrations** all live in `Infrastructure/Common/Postgres/Migrations/` as `{version}_{snake_case}.cs`, for example
  `202610240001_create_user_table.cs` with class `CreateUserTableMigration`.
- **Contracts and converters** sit in `Contracts/` and `Converters/` subfolders of the feature (Application, Api) or of
  `Repositories/` (Infrastructure); architecture tests enforce `*Internal`, `*Request`/`*Response`, `*Db` and `*Converter`.
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
  business rules and use the scoped connection and the active transaction from `PostgresTransactionAccessor`.
  The API applies migrations on startup when `Postgres:RunMigrationsOnStartup` is set (Development) and supports
  `--migrate-only` for deployments. See [ADR 0006](adr/0006-persistence-and-migrations.md).
- **Health.** `/api/health` is liveness and runs no dependency checks; `/api/health/ready` runs the checks tagged
  `ready`, including PostgreSQL.
- **HTTP errors.** Problem Details with a stable `code` and `traceId`; validation failures add `errors`. Domain
  and application exceptions carry their own `code` (`DomainException.Code`, `ApplicationExceptionBase.Code`).
- **Authentication.** Invite-only accounts with cookie sessions (`profitgraph.session`, HttpOnly, SameSite=Strict, Secure
  outside Development and Testing, 7 days sliding). Every endpoint requires a signed-in active account unless it opts
  out with `AllowAnonymous`, including unknown routes; each request re-checks that the account is still active, so
  blocking ends existing sessions. Sign-in is limited to 20 attempts per minute per client address and 10 per login,
  and an unknown or blocked login costs the same password verification as a wrong password. The first Owner comes
  from `Auth:BootstrapOwner` only while no account exists. Account management requires the Owner role.
- **Logging.** Serilog JSON to the console. Never log secrets, credentials, signatures or authentication headers.

## Numbers and Time

- Use `decimal` for money, prices, quantities, fees and balances, and decimal strings in transport contracts.
  Indicator series may use `double`; accounting and execution values never do.
- Use UTC timestamps. Simulated market time is explicit replay input, separate from `TimeProvider`.
- Backtest results are deterministic for identical inputs.

## Frontend

`frontend/` is a React 19 + TypeScript single-page application built with Vite, TanStack Router, TanStack Query,
Zustand and Tailwind CSS 4; tests use Vitest and Testing Library.

```text
src/
  app/        composition: providers, router, layout shell, global styles
  pages/      one folder per route; a page composes modules
  modules/    one folder per product area; index.ts is its public API
  shared/     domain-free code: API client, UI components, utilities
```

- **Imports** (enforced by ESLint): `shared` imports nothing from `modules`, `pages` or `app`; `modules` import
  `shared` and other modules only through their `index.ts`, never `pages` or `app`; `pages` import `modules` (through
  `index.ts`) and `shared`, never `app` or another page; `app` may import anything.
- **State.** Server state lives in TanStack Query; client UI state lives in Zustand. Do not copy server data into
  Zustand stores.
- **API client.** `shared/api/client.ts` is the only HTTP client. Its types come from
  `shared/api/generated/schema.d.ts`, generated from the API's OpenAPI document with `npm run generate:api` and never
  edited by hand. Failures surface as `ApiError` built from Problem Details.
- **Numbers.** Money, prices, quantities, fees and balances arrive as decimal strings. Never convert them to
  JavaScript `number` for arithmetic; format them for display only.
- **Time.** The API sends UTC timestamps; convert to local time only for display.
- **Texts.** User-facing texts are in Russian. The accent color is reserved for the single primary action on a screen.

## Testing

- Unit tests cover Engine, Domain and Application, including every aggregate state transition.
- Integration tests cover HTTP, persistence and hosts against the disposable test PostgreSQL
  (`docker-compose.test.yml`, port 5436). Each run creates its own database `profitgraph_test_<guid>` and drops it
  afterwards (`Tests.Integration/Common/TestDatabase`), so concurrent runs from other worktrees or agents never share
  state. `PROFITGRAPH_TEST_POSTGRES` overrides the server; development ports 5433 and 5435 are refused.
- Shouldly for assertions, NUnit for attributes and lifecycle; await asynchronous exception assertions.
- Live code is tested with mocked venue clients only.

## Code Style

Style rules come from the tracked `backend/ProfitGraph.sln.DotSettings`; the shared cleanup profile `Cleanup` is
the final check. Details are listed in [AGENTS.md](../AGENTS.md#c-code-style).
