# ADR 0012: Backtest Runs

Status: Accepted, 2026-10-25.

## Context

A backtest replays a strategy version on prepared history and must survive a closed browser, so it runs as a
background job in the Worker (ADR 0011). Profit Fox Designer froze the lowered graph with every run, persisted results
in several intermediate forms and reconciled them after consumer failures (its ADRs 0018, 0027–0029, 0045 and 0046).
ProfitGraph keeps the frozen inputs and the bounded replay pipeline, stores results directly in their final form
(stage 3 plan, S3-6) and leaves reconciliation after a storage failure out.

## Decision

- **Aggregate.** `BacktestRun` has the identifier of its job and belongs to the owner of the strategy; another user's
  strategy is reported as missing. Its stages are not started → `LoadingHistory` → `Replaying`, then a result is
  stored once. Whether the run is queued, running, cancelled or failed is the job's status, never a second status on
  the run. Every transition has Domain tests (ADR 0004).
- **Freezing** (S3-5). Starting a run stores, in the same transaction as its job: the strategy version, parameter
  values, the series, the requested period and the clock reading that fixes the latest closed candle, execution
  settings, the holdout fraction (default 0.2), the warm-up demand, the execution policy and the compilation
  fingerprint. The graph itself is not stored: versions are immutable, so the Worker compiles the version again and
  fails the run with `backtest_compilation_changed` if the fingerprint or the policy differs. A known start lets the
  holdout be checked at once (`invalid_split`). No venue is contacted when a run starts.
- **Execution.** The Worker compiles, captures instrument rules (ADR 0010), prepares history and attaches both to the
  run, then restarts job progress for the replay. The replay runs on its own thread and writes every fact into a
  bounded channel (`Backtests:ChannelCapacity`, default 4096) and waits when it is full. One consumer commits batches
  (`Backtests:BatchSize`, default 1000), each in its own short transaction, and reports progress after each commit.
- **Stored facts** are immutable rows: `backtest_ledger` (each entry as JSON with its kind, candle and order),
  `backtest_candles` (one numeric row per replayed candle: prices, account, valuation, active orders, and the ledger
  and diagnostic positions reached by then, so a reader can split facts by candle) and `backtest_diagnostics`.
  Decimals stay exact `numeric`. The run keeps fact counters, the final valuation and the final Full / IS / OOS
  metrics of the observed candles. Chart buckets and read models come with stage 3.6.
- **Endings.** Cancellation stops the replay at the reached cursor; every fact it reported is stored before the job
  records `Cancelled`. A failed replay stores its facts and fails with `backtest_replay_failed`. A storage failure
  stops the replay and fails with `backtest_persistence_failed`; the stored prefix stays. A Worker restart marks the
  job `background_job_interrupted` and the run keeps the stage it reached; runs are never resumed.
- **HTTP.** `POST /api/backtests` takes the strategy, optional version and parameters (decimal strings), the series,
  the period, settings and the holdout fraction, and answers `202 Accepted` with the job and
  `Location: /api/jobs/{id}`.

## Consequences

- A run is reproducible from its frozen configuration, the captured rules snapshot and the prepared history identity.
- An engine change that alters compilation fails queued runs instead of replaying something else.
- Reading results, progress over SignalR and chart buckets are stage 3.6; reconciliation of a partially stored
  batch after a storage failure is not ported.
