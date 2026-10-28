# ADR 0006: Persistence and Migrations

Status: Accepted, 2026-10-24.

## Context

The MVP accumulated 79 migrations in four weeks, and parallel branches repeatedly produced duplicate
date-based migration versions.

## Decision

- PostgreSQL with Dapper for data access and FluentMigrator for schema changes.
- Until the first ProfitGraph production deployment, each feature has one initial migration that is edited in
  place; afterwards, schema changes are new incremental migrations.
- Migration versions use `yyyyMMddNNNN`. An architecture test fails the build on duplicate versions.
- All migrations live in `Infrastructure/Common/Postgres/Migrations/` as `{version}_{snake_case}.cs` with a
  `*Migration` class, as in Profit Fox Designer; repositories keep their row models (`*Db`), converters and SQL text in `Repositories/Contracts`,
  `Repositories/Converters` and `Repositories/Queries`.
- Large backtest and optimization results use the compact storage formats proven in the MVP from the start.
- Development PostgreSQL runs on port 5435 (persistent); tests use a disposable tmpfs PostgreSQL on port 5436.

## Consequences

- Version collisions between parallel branches fail CI instead of failing at startup.
- The ports do not collide with a Profit Fox Designer stack running on the same machine.

## Amendment: tables of a venue module (ADR 0022, C8)

- A venue module may own tables named after its venue (`steam_*`). Their migrations live in the module's
  `Persistence/Migrations` folder with the same version scheme and naming; the module adds its assembly to the shared
  migration runner, so the API applies them with every other migration, and the duplicate-version test covers them.
- The module reads and writes them with Dapper through the registered `NpgsqlDataSource`, in short statements of its
  own; it never joins a core transaction and does not reference Infrastructure. A row that needs a core row refers to
  it by foreign key, and the core row is written first.
- Operator commands (`--run {name}` on the Worker) let a module offer a one-time console task, such as importing a
  credential, without migrations or background services.

## Amendment: production (ADR 0053)

- The first production deployment ends the rule of one initial migration per feature edited in place. From then on,
  a schema change is a new migration, and a migration already applied in production is never edited.
- `deploy/deploy.sh` applies migrations with `--migrate-only` while the previous version still runs, so a migration
  must not break the version before it.
