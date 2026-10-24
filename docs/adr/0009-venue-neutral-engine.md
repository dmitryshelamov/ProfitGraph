# ADR 0009: Venue-Neutral Engine

Status: Accepted, 2026-10-24.

## Context

In Profit Fox Designer, venue economics were written into the trading accounting core: a Steam fee calculator and
fee model, Steam acquisition holds, Steam diagnostics in the replay state, T-Invest whole-lot market buys, weekend
and synthetic candle flags, and a `Venue` string with a Bybit default in the run snapshot. Every new venue changed
the engine, and backtest code knew which exchange it simulated.

## Decision

- `ProfitGraph.Engine` has no notion of a trading venue: no venue identifiers, names or venue-specific branches.
  The architecture test from ADR 0003 applies to Engine as to every core project.
- The engine receives **instrument rules** (steps, minimums, precision) and **policies** with venue-neutral names
  (fee asset, exact quote fees, minimum-order policy). Venue modules translate exchange specifics into them.
- Wave 1 ports what Bybit needs plus neutral mechanisms that are already cleanly separated. Removed for now:
  the Steam fee model, acquisition holds, whole-lot market buys, trading sessions, weekend and synthetic candle flags.
  Their branches are deleted, not disabled.
- When wave 2 or 3 needs one of them, it returns as a neutral engine policy with its own tests, for example a fee
  model abstraction, a settlement-hold policy or a trading schedule, introduced by its own ADR.
- Engine exceptions derive from `EngineException`; the API maps them like domain rule violations.
- Engine code is organized by subsystem (`Market/`, `Accounting/`, `Replay/`, ...).
- The externally executed account, its checkpoints and graph change are pure, venue-neutral engine state machines
  shared with replay through causal dispatch (Profit Fox Designer ADRs 0049–0051). They live in the engine; the
  live runtime that feeds them exchange updates arrives with stage 5.
- History preparation evidence specific to one venue (trading-session classification, verified market closures,
  reference-instrument evidence) is not part of the engine; replay requires contiguous candles on the timeframe grid.

## Consequences

- Backtest replay is the same code for every venue; venue differences are data.
- Old tests that exercised removed venue behavior are not ported; their scenarios return with the policies.
