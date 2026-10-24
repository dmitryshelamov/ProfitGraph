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

## Amendment: results and progress (stage 3.6)

- **Reads.** Every read checks that the run belongs to the user; another user's run or strategy is reported as missing.
  `GET /api/backtests/{id}` returns the configuration, history, result, job status and fact counts. Facts come in
  bounded pages with a `next` cursor: `/observations` (candle observations after a sequence, each with the ledger and
  diagnostic ranges it owns, at most 100), `/orders` (latest state per order, by order id), `/fills` and
  `/diagnostics` (at most 500). `/chart` returns at most 5000 points.
- **Chart compaction** (Profit Fox Designer ADR 0046). The Worker turns the observations of a run whose job finished at
  least `Backtests:CompactAfter` ago (default 10 minutes, checked every `Backtests:CompactionInterval`) into chart
  buckets of consecutive candles and keeps only the last observation; ledger, diagnostics and metrics stay. Until then
  the chart is aggregated from the observations with the same SQL, so an interrupted run has a chart too. The delay
  gives live clients time to catch up before observations disappear.
- **Lists.** `GET /api/strategies/{id}/backtests` lists a strategy's runs newest first; the strategy list shows each
  strategy's latest run. `GET /api/backtests?quote=&symbol=&timeframe=` ranks the user's completed runs in one quote
  currency by full-period return; cancelled and failed runs are not ranked. List cursors are run identifiers.
- **Progress** (S3-9, S3-11). Each committed fact batch and each run save sends `NOTIFY backtest_progress` with the
  run id in the same transaction, so a signal never precedes its facts. The API listens on a dedicated connection and,
  for runs that someone follows, reads the observations committed after the last one it sent and sends them in order
  as `BacktestProgress` to the run's group on `/ws/backtests`, then `BacktestFinished` once the run has a result.
  `Subscribe(runId)` checks ownership and returns the run's last stored observation and outcome. A client buffers
  messages, reads observations after its cursor over HTTP to fill gaps (for example after a reconnect) and keeps each
  sequence once; a lost signal is caught up by the next one, and the job status remains the final word.
