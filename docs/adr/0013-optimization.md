# ADR 0013: Optimization

Status: Accepted, 2026-10-25.

## Context

Optimization searches parameter values of a strategy version, ranks candidates by an in-sample objective and keeps the
holdout out of selection. Profit Fox Designer fixed the search as a reproducibility contract (its ADR 0033), moved
ranges out of saved strategies into the request (ADR 0038) and added pause and resume (ADR 0044). ProfitGraph ports
the search bit for bit and builds the rest in stage 4 (grid and random search first, then coarse-to-fine, TPE v1 and
walk-forward). This ADR records the search core; later stage 4 pull requests amend it.

## Decision

- **Ranges belong to the request** (S4-2). A strategy version stores fixed values only (ADR 0007). A request gives
  ranges by parameter name; `BacktestCompiler` lowers them into the effective graph, rejects an unknown name
  (`unknown_parameter`, field `Ranges.{name}`) and a parameter that is both fixed and ranged
  (`parameter_fixed_and_ranged`). A dimension is identified by the parameter's external reference
  `(parameterName, "value")`, which composite expansion preserves.
- **Search space.** `OptimizationSearchSpace` reads every decimal range of the compiled graph; a range on a wired input
  is invalid. A range is exact decimal arithmetic: `min`, `max` and a positive `step` on the numeric value types, whole
  values for `Integer`, and `max - min` divisible by the step. The grid is mixed radix over dimensions ordered by node
  id, then port key (ordinal); the last dimension changes fastest.
- **Enumeration.** The default budget is 2000 trials, at most 1 000 000. A grid within the budget is enumerated in
  order. A larger grid, or a forced random search, draws without replacement with a seeded sparse partial
  Fisher–Yates over SplitMix64 v1 (`NextBelow` by rejection on a mask). Seed 42, grid 0..99 and budget 10 give
  `[21, 4, 84, 23, 10, 98, 42, 92, 54, 72]`; this vector and the archive's others are pinned in unit tests. Changing
  the algorithm requires a new method version.
- **Objective and constraints.** Objectives are in-sample return percent (default), total PnL and return to drawdown;
  an overflowing value is a null objective. Default constraints are at least one execution and at most 50 % drawdown.
  A trial is completed, rejected by a constraint, rejected by a template rule or failed.
- **Ranking.** Objective descending, null last, then the canonical coordinates of the grid; identical inputs give an
  identical ranking.
- **Template rules.** A strategy marked with a template checks the relations its template needs (for example
  `fastPeriod < slowPeriod`, `0 < drop < 1`, `buyAmount <= allocationCap`) on static values before a candidate is
  replayed, and every rule's parameters must be positive. Unlike the archive, a parameter the strategy no longer has
  skips its rule instead of failing, so editing a template never makes it unrunnable.

## Consequences

- An optimization is reproducible from its request, seed and the frozen inputs of stage 3.
- The search lives in Domain (`ProfitGraph.Domain.Optimization`) on top of the Engine, so trials, storage and the
  Worker lane in later pull requests reuse it unchanged.
- Coarse-to-fine, TPE v1, walk-forward, trials, storage, progress and pause are recorded by amendments to this ADR and
  by ADR 0014.

## Amendment: trials (stage 4.2)

- **Metrics-only trials** (S4-4). `OptimizationTrialRunner` replays each trial on the in-sample slice of one shared
  prepared history with one shared compiled graph, a fresh metrics observer per trial and no retained ledger. The
  holdout is never replayed during selection.
- **Prefilter.** Candidates are checked against the template rules in canonical order before they are replayed. A
  rejected candidate is delivered as `RejectedByTemplate` and does not consume the budget. When the candidate limit is
  examined without enough valid trials in a grid larger than that limit, the search fails with
  `candidate_limit_exceeded` after delivering the rejected prefix.
- **Parallelism.** At most `Optimization:MaxWorkers` trials (default 8) replay at once. Results go through a bounded
  channel (`Optimization:ChannelCapacity`, default 256) to one serial receiver, which also gets an idle flush at least
  once a second for timed batch commits. The ranking does not depend on the worker count or completion order.
- **Failures and cancellation.** A failed replay keeps its engine diagnostic code; an unexpected exception becomes
  `trial_exception`; the other trials continue. Cancellation stops new trials, and every result already produced is
  delivered before the run returns as cancelled. A receiver failure cancels the workers and is rethrown.
- **Throughput.** The explicit `OptimizationPerformanceTests` (Release, Server GC, 24 trials per sample) extrapolate
  1000 trials of 8760 hourly candles at eight workers to about 4 s for SMA and 48 s for Ladder on the reference host
  (2026-10-25), within the archive targets of 30 s and 10 minutes (archive: 8.5 s and 399 s). The estimate excludes
  persistence and finalists.

## Amendment: jobs, storage and finalists (stage 4.3)

- **Aggregate** (S4-1). `OptimizationRun` has the identifier of its job and belongs to the owner of the strategy;
  another user's optimization is reported as missing. Its stages are not started → `LoadingHistory` → `Searching` →
  `StartingFinalists`. It holds the planned trials, the counters by status and the best trial. Whether it is queued,
  running, cancelled or failed is the job's status. Every transition has Domain tests.
- **Freezing** (S4-2). `POST /api/optimizations` takes a backtest request, ranges by parameter name, the method
  (`auto`, `grid` or `random`), objective, budget, seed (a decimal string), finalist count and constraints. It
  freezes the backtest configuration exactly as a run does, with the fingerprint of the graph compiled with the
  ranges, and stores the optimization with its job in one transaction. `auto` enumerates a grid within the budget
  and samples a larger one. `grid` over budget is `invalid_optimization`. An omitted seed is drawn at random and
  stored. The job runs in the `optimization` lane (ADR 0011).
- **Execution.** The Worker recompiles with the frozen ranges and fails with `optimization_compilation_changed` on a
  different fingerprint. It prepares history once and runs the trials on its in-sample part (amendment: trials).
- **Storage** (S4-7). Trials are stored in `optimization_trials` in their final form, in batches of
  `Optimization:BatchSize` (default 500) or at least every second: delivery sequence, trial index, ranged values by
  name, status, failure code, objective, in-sample return, PnL, drawdown, executions and elapsed time. Each batch is
  committed with the counters and the best trial in one short transaction. A batch retried under the same sequences
  changes nothing. Cancellation keeps every stored trial, and the job records `Cancelled`.
- **Finalists** (S4-5). After the search the best completed trials (default 3, at most 10) start as ordinary runs
  of the whole prepared period. Each finalist has the trial's values fixed, the frozen clock reading and holdout,
  and `backtest_runs.optimization_id`. They run in the main lane like any other run.
- **HTTP.** `GET /api/optimizations/{id}` returns the configuration, stage, history, counters, best trial, job
  status and finalist runs. `/trials` pages by delivery sequence (at most 500). `GET /api/strategies/{id}/optimizations`
  lists a strategy's optimizations, newest first.

## Amendment: progress (stage 4.4)

- **Signals** (S4-12). Every committed save of an optimization — a trial batch with its counters, or a stage —
  notifies `optimization_progress` with its identifier. Every job that finishes, including one cancelled while queued
  or failed by a Worker restart, notifies `background_job_finished`. The API listens on both channels on a connection
  of its own (`LISTEN/NOTIFY`, shared with backtest progress).
- **Hub.** `/ws/optimizations` is a separate hub from `/ws/backtests`, so each feature owns its messages; the stage 4
  plan named the backtest hub. `Subscribe(id)` checks ownership, joins the optimization's group, then returns the
  cursor: the last stored trial sequence and the job's status. Joining before reading leaves no gap. For each signal
  the API sends `OptimizationProgress` with every trial stored after the last one sent, in delivery order, then
  `OptimizationFinished` with the job's status once it has ended. A client that reconnects reads the trials after its
  cursor from `/api/optimizations/{id}/trials` and drops the sequences it already has.
