# Roadmap

Rebuild of Profit Fox Designer ([ADR 0001](adr/0001-rebuild-from-profit-fox-designer.md)). Each stage is a series of
small pull requests; a stage is done when its acceptance criterion holds.

## Wave 1: Bybit end to end

| Stage | Scope | Done when | Status |
|---|---|---|---|
| 0. Skeleton | Solution and projects, MediatR pipeline, Problem Details, health, architecture tests, CI, compose, documents; frontend skeleton | CI is green, `/api/health` responds, the frontend builds | Done |
| 1. Engine | Graph, blocks, lowering, validation, compilation, evaluation, dispatch, accounting, replay, metrics, templates | Ported tests and regression fixtures pass; templates on the same candles produce the same ledger as the MVP | In progress: accounting, graph and node types |
| 2. Strategies and accounts | Strategy versions, block catalog API, users, sign-in, sessions, rate limits; persistence foundation, transaction behavior, migration version test | Strategy CRUD and sign-in work through API and UI | Planned |
| 3. Data and backtests | Venue ports, `Venues.Bybit` public data, CSV import, background jobs, durable runs, progress, results, comparison, holdout | A template backtest on Bybit data runs from pair selection to result charts | Planned |
| 4. Optimization | Grid, coarse-to-fine, TPE, walk-forward, durable trials, resume | Walk-forward optimization completes and resumes after restart | Planned |
| 5. Live core | Bot and order-intent aggregates, Worker, leases and fencing, checkpoints, outbox, reconciliation, risk limits, credential encryption, Bybit order gateway (trading disabled by default), manual orders, graph change, event feed, positions, account snapshots, Telegram alerts | Bot scenarios pass against a mocked gateway; every transition has Domain tests | Planned |
| 6. Production | Production compose, watchdog, runbook, switch-over | The owner deploys, enters credentials and starts bots | Planned |

## Wave 2

- `Venues.TInvest`: trading sessions, lot sizing, price bands, day orders and carry-over, late commission
  corrections, synthetic candles for empty intervals. Confirms that venue ports need no core changes.
- Telegram bot control on top of the wave 1 alerts.

## Wave 3

- `Venues.Steam` with its own model (marketplace fees, holding periods, mobile confirmations). Starts with an ADR
  describing the model and the venue ports it uses.
