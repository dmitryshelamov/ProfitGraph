# ADR 0041: The Daily Loss Baseline Carries Over Midnight

Status: Proposed, 2026-10-28. Amends the daily loss rule of ADR 0018. The owner reviews it in the pull request.

## Context

ADR 0018 measured a bot's daily loss from the first live candle root of each UTC day and killed the bot with `DailyLossLimit`
only on a later root of the same day. A review of `main` found two holes:

- A bot on the `1d` timeframe has one root a day, so every root set a new baseline and the limit could never fire.
- On every timeframe, the loss between the last root of one day and the first root of the next was never counted: the new
  baseline absorbed it. A bot could lose most of its allocation over a night, a weekend or a runner outage without a kill.

## Decision

- **Carry-over.** The bot stores, beside the day and its baseline equity, the equity of its last recorded root
  (`DailyLossBaseline.LastEquity`, column `bots.day_last_equity`). The first root of a later UTC day takes that last equity as
  the new day's baseline instead of its own equity.
- **Every root is checked.** The first root of a day is checked against the carried baseline like any other root, so a loss
  that reaches `DailyLossLimitQuote` kills the bot with `DailyLossLimit` on that root, in the same commit (ADR 0018).
- **Start.** The first root after a start has nothing to compare with and sets the baseline to its own equity; a start still
  clears the baseline. A restart after a runtime failure keeps it, so a loss while no runner held the bot is counted at the
  next root.
- **Daily timeframe.** On `1d`, the longest timeframe, each root's loss is measured from the previous root, so the limit
  bounds the loss of one daily candle. On shorter timeframes the loss of a day is measured from the previous day's last root.
- **Stored bots.** The migration gives a stored baseline its own baseline equity as the last equity, the best value known; the
  next day then measures from it.

A rolling 24-hour window was considered. It needs the equity history of the last day for every bot and changes what "daily"
means for the owner; the carry-over closes both holes with one more column.

## Consequences

- `Bot.RecordDayEquity` changes the bot at every live root (the last equity and `UpdatedAt`), as `ResetRestartStreak` already
  did, so no extra write happens.
- A loss spread over many days in steps below the limit still never kills the bot; the limit is a daily bound, not a drawdown
  bound.
- A day without candles (a closed MOEX session, a weekend) is not a day of its own: the first root after it measures from the
  last root before it.
