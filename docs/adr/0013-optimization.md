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
