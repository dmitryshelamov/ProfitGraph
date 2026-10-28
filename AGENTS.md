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

- Owner authorization: explicit permission, approval or a direct instruction in chat authorizes the requested
  exchange, broker or production work. Informal wording such as "go ahead", "approved", "do it", "добро", "апрув" or
  "требую" counts when it refers to that work. A direct instruction is itself authorization; do not require a
  separate approval phrase. Without such authorization, do not access real credentials or their master keys,
  make authenticated exchange or broker requests, or access production servers and databases.
- Authorized exchange and broker work may include any interaction covered by the owner's instruction: reading
  reference and account data, placing, amending and cancelling real orders, changing bot settings and desired
  state, enabling live trading, and other state-changing API requests. There is no blanket prohibition on
  manual real-order operations when the owner authorizes them. Permission limited to read-only work remains
  read-only; broad permission to perform any exchange interaction covers both reads and changes.
- Credential access for authorized work: agents may retrieve exchange or broker keys, tokens and secrets from
  the development PostgreSQL (`localhost:5435`), the production database, the owner's other databases or
  environments, read the required master key, decrypt stored credentials, and use them for the authorized work. The
  same applies to other secrets the requested work needs. Authorization for exchange, broker or production work
  includes this necessary credential access; do not ask separately for each key, decryption or request. Use the
  application's credential path or a throwaway script outside the repository. Reading credentials does not
  authorize unrelated database writes, cleanup or deletion.
- Authorization covers the agreed scope and its required sequence of calls, including necessary follow-up
  calls. Proceed without repeated confirmation within that scope. Ask again only for actions outside the
  authorized scope; stop if the owner revokes authorization. These project rules permit owner-authorized work
  but do not override tool permissions or higher-priority platform restrictions.
- Secret handling: keep decrypted credentials and master keys in memory only. Never commit secrets or write them
  to repository files, logs or test fixtures. Do not print secret values or include them in chat unless the owner
  explicitly requests and approves disclosure of those specific values. Never print or log signatures or
  authentication headers. Report only the response fields needed for the approved task; full account IDs may
  be shown when the owner requests them.
- Live trading is disabled by default in committed configurations; enabling it, including in a committed
  configuration, requires an explicit owner instruction covering that change. Tests use mocked venue clients only.
  Never put secrets, keys or tokens in the repository, logs or test fixtures.
- Engine and backtest replay must not depend on live code or venue modules.
- Use public market data only when a task requires it.
- Automated database tests that change state may use only the test PostgreSQL from `docker-compose.test.yml`
  (`localhost:5436`, tmpfs, disposable). This does not restrict owner-authorized exchange or broker operations
  described above. Do not drop, clean or manually modify the development database (`localhost:5435`, persistent
  volume) without explicit permission. Reading its credentials for authorized work is permitted as described above.
- Production access: when the owner asks, agents may work with the production environment the same way as with the
  local and development ones. This includes connecting to production servers over SSH, connecting to the
  production database, reading logs, configuration and data, running diagnostics, deploying, restarting services
  and making the other changes the instruction covers. Authorization and scope follow the rules above; a direct
  instruction is enough. Production hosts' credentials, SSH keys and connection strings are secrets and follow the
  secret-handling rule above. Dropping, cleaning, deleting or bulk-modifying production data requires explicit
  permission for that action, as for the development database. Automated tests never run against production.
- The Profit Fox Designer stack (ports 5173, 5191, 5433, 5434) may run on the same machine. Do not stop or modify it
  as part of ProfitGraph work.

## Build and test

```bash
dotnet build backend/ProfitGraph.sln
dotnet test backend/ProfitGraph.sln
```

Integration tests need the test PostgreSQL. Each run creates and drops its own database there, so the container may
stay up between runs and be shared by parallel worktrees:

```bash
docker compose -f docker-compose.test.yml up -d --wait
dotnet test backend/ProfitGraph.sln
```

Frontend checks run in `frontend/` (Node `^22.22.2 || ^24.15.0 || >=26`; CI uses Node 24):

```bash
npm ci
npm run lint
npm run format:check
npm run typecheck
npm test
npm run build
```

## Local development

- API: `dotnet run --project backend/src/ProfitGraph.Api --launch-profile http` on `localhost:5291`.
- Worker: `dotnet run --project backend/src/ProfitGraph.Worker` runs background jobs (ADR 0011); without it jobs stay
  queued and `/api/health/ready` reports `Degraded`.
- macOS and T-Invest: .NET on macOS does not complete the TLS handshake with `invest-public-api.tbank.ru` (cause not
  proven), so run the API and Worker in Linux containers with `docker compose -f docker-compose.dev-app.yml up -d --build --wait`
  and stop them with `docker compose -f docker-compose.dev-app.yml down` (never `-v`). Details: `docs/dev-containers.md`.
  Stop the host-run API first (port 5291). Do not modify or stop the dev database `5435` as part of container
  management; authorized credential reads follow the Safety rules above. Do not touch the Profit Fox Designer
  stack (5173, 5191, 5433, 5434).
- Frontend: `npm run dev` in `frontend/` on `localhost:5273` (`strictPort`); `/api` and `/ws` are proxied to the API.
  Regenerate the API client with `npm run generate:api` while the API runs.
- Development PostgreSQL: `docker compose -f docker-compose.dev.yml up -d --wait` on `localhost:5435`.
- End-to-end tests: `npm run test:e2e` in `frontend/` with the test PostgreSQL running. Playwright starts its own API
  (`127.0.0.1:5293`) and frontend (`127.0.0.1:5275`) against a disposable `profitgraph_e2e_<hex>` database on 5436
  with a per-run Owner password, and drops the database afterwards; it never touches 5435.
- Stop temporary instances started for checks; do not leave stray processes.
- Production: `docker-compose.prod.yml`, `deploy/` and the `Production` workflow ([ADR 0053](docs/adr/0053-production-deployment.md));
  operations are in `docs/production.md`.

## Architecture and code style

`docs/architecture.md` is binding. Violations of documented rules are review findings, not style preferences.
Key invariants:

- Project dependencies: `Api, Worker -> Infrastructure, Venues.* -> Application -> Domain -> Engine`.
- Folders are features first, then use cases; namespaces follow folders. Follow the template in
  `docs/architecture.md#folder-conventions` and never add top-level `Commands/`, `Entities/` or similar type folders.
- One type per file, no nested types. Application contracts are `*Internal` in `<Feature>/Contracts` with
  `*InternalConverter` in `<Feature>/Converters`; HTTP contracts are `*Request`/`*Response` with `*ResponseConverter`.
  Repositories follow `<Feature>/Repositories/{Contracts,Converters,Queries}` with `*Db` row models. Migrations are
  `{version}_{snake_case}.cs` with a `*Migration` class in `Infrastructure/Common/Postgres/Migrations/`; a venue module
  that owns `<venue>_*` tables keeps their migrations in its `Persistence/Migrations/` folder (ADR 0006).
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
