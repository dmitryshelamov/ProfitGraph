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
| `Api`, `Worker`  | Composition roots: HTTP boundary; background jobs and, from stage 5, the live runtime.                 | Contain domain rules or direct persistence access.              |

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
  Strategies/                 Strategy.cs, StrategyVersion.cs, StrategyId.cs, StrategyName.cs, IStrategyRepository.cs, ...
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
  Strategies/
    Repositories/             StrategyRepository.cs, StrategyVersionReader.cs
      Contracts/              StrategyDb.cs, StrategyVersionDb.cs, StrategySnapshotDb.cs (jsonb snapshot)
      Converters/             StrategyDbConverter.cs, StrategySnapshotDbConverter.cs (versioned JSON)
      Queries/                StrategyQueries.cs, StrategyVersionQueries.cs
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
  BybitVenue.cs (venue descriptor), BybitRegistration.cs (AddBybitVenue), MarketData/ (public history),
  Instruments/ (pairs and rules), later Trading/
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
- **Outbox.** A transaction stores the Domain events of the aggregates it wrote in `outbox_messages`; the Worker
  delivers each to MediatR notification handlers once, in the transaction that marks it delivered. Handlers write only
  to the database. See [ADR 0016](adr/0016-live-bot-runtime.md).
- **Value objects.** Entities hold identifiers and constrained values as value objects, not primitives: typed IDs
  (`StrategyId`, `UserId`) and numbers (`StrategyVersionNumber`) are `readonly record struct`s, and constrained text
  (`StrategyName`, `UserLogin`) is a sealed record. A value object validates in its constructor, so an invalid value
  cannot reach an entity. Application contracts (`*Internal`) and row models (`*Db`) keep primitives; their converters
  unwrap `.Value` and wrap on the way back. Handlers create value objects from request primitives. An architecture
  test rejects raw `Guid` properties on Domain types.
- **Background jobs.** Long work (downloads, backtests) is a job request (`IBackgroundJobRequest<TResult>`, always
  non-transactional) queued by a command and run by the Worker; the API never runs it. Progress goes through
  `IBackgroundJobProgress` (`Restart` begins the next stage); cancelling a running job sets a flag the Worker polls.
  Work that a record describes, such as a backtest run, is queued under the record's identifier. See
  [ADR 0011](adr/0011-background-jobs.md).
- **Backtest execution.** The engine replays on its own thread and reports facts to `BacktestReplaySession`, which
  commits them in bounded batches, each in its own transaction, while the replay waits on a full channel. Each commit
  sends `NOTIFY backtest_progress`; the API listens and pushes committed observations to `/ws/backtests` (SignalR),
  and clients fill gaps over HTTP. See [ADR 0012](adr/0012-backtest-runs.md).
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
- **Secrets at rest.** Exchange credentials are encrypted per value with AES-256-GCM under master keys configured
  outside the repository; no response or log contains a secret. See [ADR 0015](adr/0015-exchange-credentials.md).
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
- **Session.** The signed-in user is server state: `modules/accounts` loads `/api/auth/me` into TanStack Query (null
  without a session). The router's `_authenticated` layout route guards every screen except `/sign-in` in
  `beforeLoad` and redirects there with `returnTo`. Sign-in and sign-out clear the whole query cache, so no data of a
  previous user survives; any 401 from a query or mutation means the session expired and sends the user back to sign
  in. Only same-application paths are accepted as `returnTo`.
- **Strategy editor.** `modules/strategy-editor` draws the block definition as a flowchart with React Flow. The draft,
  selection and undo/redo history live in a Zustand store; every change is an editor command, so undo restores exact
  drafts. Saving sends the version the draft started from; `strategy_changed` keeps the draft and offers to reload.
  The editor route is guarded like other screens but renders without the navigation shell.
- **Background work.** `modules/jobs` follows a job by polling `/api/jobs/{id}` every second until it finishes and
  offers cancellation; `JobProgress` shows any job. History downloads and run starts hand their job to it.
- **Run setup.** `modules/backtests` keeps the form as typed text: percents become fractions and dates become UTC
  midnight by string arithmetic (`shared/lib/decimal`), never through `number`, before the request is sent. A run
  replays a saved version, so the editor offers it only without unsaved changes.
- **Run results.** `modules/backtests` shows a run from its job: while the job is unfinished, `useRunProgress`
  follows `/ws/backtests` (SignalR) and `ProgressBuffer` keeps each observation once and in order, filling gaps from
  `/observations` before showing anything after them; once the job finishes, the page reads the result, the chart
  and the fact pages. `modules/market-chart` draws with lightweight-charts, the one place where decimal strings become
  numbers, for drawing only; every exact value on the page comes from the strings.
- **Optimization.** `modules/optimizations` sets up a search around the backtest form: each parameter is either
  fixed or ranged (from, to, step, typed as text and converted like the run setup), with the method, objective,
  budget, constraints and optional walk-forward windows. While its job is unfinished, `useOptimizationProgress`
  follows `/ws/optimizations` and `SequenceBuffer` (`shared/lib`) keeps the latest trials once and in order, filling
  gaps from `/trials`. Pause and resume go through the optimization's own endpoints. Once the job finishes, the
  page reads every trial, the walk-forward windows and summary, and links the finalist runs.
- **Comparison.** `modules/comparison` keeps the runs and trials picked for comparison in a Zustand store persisted
  to the tab's session storage, a viewer convenience, and shows the count with a link to `/compare`. The page reads its
  items from the address, loads each run and trial through their own modules, and draws the runs' equity with
  `EquityComparisonChart` (`modules/market-chart`). Tables take an `action` column so pages add the picking checkbox
  without modules importing each other in a cycle.
- **Live bots.** `modules/credentials` adds and removes exchange keys: the secret is sent once and the list shows only a
  key's last four characters. `modules/bots` lists and creates bots (a strategy's parameters start from its defaults),
  follows a bot's state by polling every five seconds and offers start, stop and the kill switch, which asks to
  confirm. Risk limits change at any time; parameters, the bot's capital and foreign orders only while it is stopped.
  The bot page lists its alerts with acknowledgement and sends an owner manual order, followed until the runner
  resolves it. `TradingHaltBanner` shows the global halt to everyone and lets the Owner halt or resume. When a save is
  refused with `live_impact`, the editor lists the running bots and saves again with `confirmLiveImpact`.
- **Live reports.** The bot page shows the position, the feed and the fills; the feed and the fills page backwards
  through `before` cursors. Its chart (`BotChart`, drawn by `CandleHistoryChart` in `modules/market-chart`) shows the
  bot's closed candles on its own timeframe from `/api/bots/{id}/candles`, 500 at a time going back as the view nears the
  oldest one, merged by open time, with the bot's fills as markers at their prices and its working limit orders as dashed
  levels; candles closed since the newest page arrive every minute ([ADR 0026](adr/0026-bot-chart.md)). The position card subscribes to `/ws/live-prices` for the bot and shows a pushed ticker
  only when it is newer than the stored display price, with the price's source and age, and then recomputes the
  unrealized profit as the server does (base total × price − cost basis) in exact decimal strings. `CredentialsPage` shows each key's latest account value, its coins and why the last
  read failed. `modules/notifications` and the `/profile` page, opened from the login in the header, issue a Telegram
  link code and unlink the chat.
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
- Frontend flows are tested through the whole application (`src/test/renderApp.tsx`: real router, guards and queries)
  against a routing table that replaces `fetch` (`src/test/fakeApi.ts`); unrouted requests answer 404. These tests
  live under `src/app`, the only layer allowed to import the router.
- End-to-end tests (`frontend/e2e`, Playwright) drive the real API and frontend against a disposable database on the
  test PostgreSQL, with the Worker and a local Bybit stub (`e2e/bybit-stub.ts`: REST and the public stream with a fixed
  ticker), so no test reaches the exchange. They cover the stage 3 acceptance path: pick a pair, download its history,
  run a template and read its chart; and the stage 5 one: a bot trades against the Worker's in-memory simulation
  (`Live:Simulation`, Testing only, ADR 0017) and its page shows the fill, the feed and the position at the live price. The block catalog and template fixtures under `src/test` are captured from the ProfitGraph API;
  refresh them when the catalog or templates change.
- `OpenApiDocumentTests` keep the OpenAPI document exact (integers are `integer`, enums are names), because the
  frontend types are generated from it.

## Code Style

Style rules come from the tracked `backend/ProfitGraph.sln.DotSettings`; the shared cleanup profile `Cleanup` is
the final check. Details are listed in [AGENTS.md](../AGENTS.md#c-code-style).
