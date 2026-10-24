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
