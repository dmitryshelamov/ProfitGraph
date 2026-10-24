# Project instructions

ProfitGraph is a visual trading-strategy designer with reproducible backtesting, parameter optimization and live
bots. It is a module-by-module rebuild of Profit Fox Designer (ADR 0001); the archive is the
`profit-fox-designer` repository at `mvp@f26ab20d`. Read `docs/roadmap.md` for what exists now and what comes next.

## Sources of truth

- Architecture and binding engineering rules: `docs/architecture.md`.
- Decisions: `docs/adr/` (next number = highest existing + 1). Domain glossary: `CONTEXT.md`.
- Order of work and status: `docs/roadmap.md`.
- When porting a module, the archived code, tests and documents are the behavioral specification. Keep behavior
  unless the change is deliberate and recorded in the pull request.
- Write `CONTEXT.md`, ADRs and `docs/` in English; keep `README.md` in Russian.

## Safety

- Agents never request, inspect, copy, store or log real exchange keys, call authenticated exchange endpoints
  directly, or manually place, amend or cancel real orders. The owner controls bot settings and authorizes changes
  to each bot's desired state.
- Live trading stays disabled in every committed configuration. Tests use mocked venue clients only.
  Never put secrets, keys or tokens in the repository, logs or test fixtures.
- Engine and backtest replay must not depend on live code or venue modules.
- Use public market data only when a task requires it.
- State-changing checks may use only the test PostgreSQL from `docker-compose.test.yml` (`localhost:5436`, tmpfs,
  disposable). Do not drop, clean or manually modify the development database (`localhost:5435`, persistent volume)
  without explicit permission.
- The Profit Fox Designer stack (ports 5173, 5191, 5433, 5434) may run on the same machine. Do not stop or modify it
  as part of ProfitGraph work.

## Build and test

```bash
dotnet build backend/ProfitGraph.sln
dotnet test backend/ProfitGraph.sln
```

Integration tests that need PostgreSQL (from stage 2) run against the test database:

```bash
docker compose -f docker-compose.test.yml up -d --wait
dotnet test backend/ProfitGraph.sln
docker compose -f docker-compose.test.yml down
```

## Local development

- API: `dotnet run --project backend/src/ProfitGraph.Api --launch-profile http` on `localhost:5291`.
- Development PostgreSQL: `docker compose -f docker-compose.dev.yml up -d --wait` on `localhost:5435`.
- Stop temporary instances started for checks; do not leave stray processes.

## Architecture and code style

`docs/architecture.md` is binding. Violations of documented rules are review findings, not style preferences.
Key invariants:

- Project dependencies: `Api, Worker -> Infrastructure, Venues.* -> Application -> Domain -> Engine`.
- Folders are features first, then use cases; namespaces follow folders. Follow the template in
  `docs/architecture.md#folder-conventions` and never add top-level `Commands/`, `Entities/` or similar type folders.
- Only `ProfitGraph.Venues.*` projects name concrete venues (ADR 0003).
- Aggregates change state only through their methods; repositories contain no business rules (ADR 0004).
- Use cases are MediatR commands and queries; MediatR stays on 12.5.0 (ADR 0005).
- Use `decimal` for money, prices, quantities, fees and balances, and decimal strings in transport contracts.
- Use UTC timestamps. Keep simulated market time as explicit replay input, separate from `TimeProvider`.
- Backtest results must be deterministic for identical inputs.

## C# code style

Style rules come from the tracked `backend/ProfitGraph.sln.DotSettings`. Its inspection severities, naming rules,
formatting, member layout and the cleanup profile `Cleanup` apply to all C# you write or edit:

- Braces are required for `if`/`else`, `for`, `foreach`, `while`, `do`, `using`, `lock` and `fixed`; do not add
  redundant braces elsewhere.
- Use named arguments for non-string literal values (numbers, booleans) in calls with more than one argument.
  Pass string literals positionally.
- Use `var` when the type is evident, and explicitly typed creation everywhere: `var x = new Foo()`, and
  `private readonly Foo _foo = new Foo();` for fields and properties, never target-typed `new()`.
- Private instance fields: `_camelCase`. Private static fields: `PascalCase`.
- Write explicit access modifiers; no redundant `this.`. Seal classes not designed for inheritance
  (`internal sealed class` by default).
- Prefer readonly fields and get-only auto-properties; remove unused usings and redundant code.
- Member order: static fields and constants, fields, properties and indexers, constructors, other members, nested
  types. In test fixtures, setup and teardown methods come first.
- Put field and accessor attributes on their own lines.
- Formatting: 170-character limit; chop long argument, parameter and call-chain lists; always chop LINQ queries.
  One blank line before block statements, control-transfer statements and single-line comments, and between using
  groups. No tabs or trailing whitespace.
- XML documentation: `<summary>` tags on separate lines, text indented as `///     Text.`.

Before handoff, clean up only the C# files you changed, then review the diff:

```bash
dotnet tool restore
dotnet jb cleanupcode backend/ProfitGraph.sln --profile="Cleanup" --include="<changed files>"
```

`--include` takes `;`-separated paths or Ant-style wildcards. Never run cleanup on the whole solution.

## Tests

- Use Shouldly for assertions; NUnit for attributes and lifecycle.
- Await asynchronous exception assertions (`await Should.ThrowAsync<TException>(...)`).
- Every aggregate state transition has a Domain unit test.

## Git

- `main` changes only through pull requests with a green CI; pull requests are squash-merged (ADR 0008).
- Pull request titles use Conventional Commit prefixes (`feat:`, `fix:`, `docs:`, `test:`, `chore:`, `refactor:`).
- One pull request carries one feature or module together with its tests and documentation.
- Do not alter staged changes you did not make.
