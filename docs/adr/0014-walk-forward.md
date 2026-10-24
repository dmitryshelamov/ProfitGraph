# ADR 0014: Walk-Forward Optimization

Status: Accepted, 2026-10-25.

## Context

An optimization that ranks one in-sample search can fit noise. Profit Fox Designer validated parameters by
walk-forward analysis: search one range, test the winner on the next unseen range, and repeat. Its runner, plan,
equity observer and summary are deterministic for frozen inputs and covered by tests. ProfitGraph ports them bit for
bit on top of the search core and trial storage of ADR 0013. The stage 4 criterion is that a walk-forward
optimization completes and resumes after a Worker restart.

## Decision

- **Settings.** An optimization request may carry `walkForward`: 2 to 50 windows (default 6), a train-to-test ratio of
  1 to 20 (default 4) and the `rolling` (default) or `anchored` scheme.
  - The windows split only the in-sample range; the holdout stays outside walk-forward as it stays outside any search.
  - The test length is the in-sample length divided by ratio plus windows; leftover candles start the first train
    range.
  - Rolling train ranges keep their length. Anchored ones start at the first candle and grow.
  - With a known start, the request is checked at once (`walk_forward_invalid_windows`). The budget applies to each
    window, and all windows together must stay within 1 000 000 trials.
- **Execution.** `WalkForwardRunner` searches each window's train range with the frozen method and a seed derived per
  window (`OptimizationSeeds`).
  - The best completed trial wins its window and is replayed on the test range with a fresh account and a bounded
    equity observer: at most 2000 points, each ending a bucket of candles.
  - A window has no winner when no train trial completes within the constraints, and fails its test when the replay
    fails; later windows still run.
  - The summary holds total test PnL, compounded test return, profitable windows, walk-forward efficiency (per-bar test
    return against train return, with diagnostics when undefined) and per-parameter winner stability in grid steps.
- **Storage.** Trials keep their window: indexes start over in each window, and delivery sequences stay global.
  - A finished window is stored in `optimization_windows` with its ranges, winner, values, train and test metrics,
    status and equity.
  - The summary is stored with the optimization when the search ends.
  - Neighborhood scores are stored per window.
- **Resume.** A resumed walk-forward optimization keeps its finished windows and continues the first unfinished one from
  its stored trials (ADR 0013, pause and resume). When a winner is restored, only its train replay runs again, to
  rebuild its full metrics. The finalist is the last window's winner, at most one.
- **HTTP.** `GET /api/optimizations/{id}` returns the walk-forward settings and summary. `/windows` returns the finished
  windows, and trials carry `windowIndex`.

## Consequences

- For identical frozen inputs a walk-forward optimization stores the same trials, windows, summary and finalist whether
  it ran uninterrupted, was paused or was interrupted by a Worker restart.
- The holdout fraction and walk-forward test ranges are independent: the holdout remains unseen by the whole search,
  and the finalist run reports it.
