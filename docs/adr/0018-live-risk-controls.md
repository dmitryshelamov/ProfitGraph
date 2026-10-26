# ADR 0018: Live Risk Controls

Status: Accepted, 2026-10-25.

## Context

Live bots deliver orders through a venue gateway (ADRs 0016 and 0017). Profit Fox Designer ADR 0056 required, before
trading may be enabled, a per-order limit, a daily loss limit, a kill switch per bot and a global halt. ProfitGraph
already models the limits, the daily baseline, kills and the halt in its `Bot` and `TradingHalt` aggregates; this ADR
decides how the runtime, the order delivery and the API apply them, and how the owner learns about them.

## Decision

- **Per-order limit.** Every bot has a positive `MaxOrderQuote`. Before the first place of a submit, the dispatcher
  values it in quote: a market buy by its budget, a limit buy by price times quantity. A buy above the limit is
  rejected with `RiskLimit` before anything is sent; the rejection is stored and reaches the engine like a venue
  rejection. A sell is not valued against the limit (see the amendment). A lookup of a submit that may have been sent
  is never blocked.
- **Allocation.** The engine's allocation cap already rejects a buy that would exceed it, before an intent exists.
- **Balance.** A venue rejection for an insufficient quote or base balance degrades the bot with
  `InsufficientBalance`; a start after the owner funds the account re-arms it. A pre-submit wallet read is left for
  the account balance reader of stage 5.14.
- **Daily loss.** At every root after a live candle, the bot records its equity at the candle's close: quote total
  plus base total at the close price. The first root of a UTC day, by the candle's close time, sets that day's
  baseline; a later root whose loss from it reaches `DailyLossLimitQuote` kills the bot with `DailyLossLimit` in the
  same commit. A start clears the baseline.
- **Kill.** `POST /api/bots/{id}/kill` stops the bot at once with `Killed`. In the same transaction every submit that
  never left the process is rejected with `KillSwitch`, and every order that is not final and may be at the venue
  gets one cancel marked as a kill (`BotKillSwitch`). Holdings are never sold. A root a killed bot still commits
  applies the same rules, so a submit it emits never leaves. Only an explicit start clears a kill.
- **Global halt.** The Owner halts (`POST /api/trading-halt/halt`) and resumes (`.../resume`); every user reads it
  (`GET /api/trading-halt`). A halt blocks every submit claim and every start (`trading_halted`) and kills every bot
  that should run. Resuming starts no bot.

- **Alerts.** The outbox (ADR 0004) turns a degradation (`BotDegraded`), a kill or daily-loss kill (`BotKilled`), a
  `RiskLimit` rejection and the first `ExchangeUnavailable` of a streak into one durable `bot_alerts` row for the bot's
  owner. An alert names a kind and the status or rejection reason by name; it never holds credentials or venue
  payloads. `GET /api/alerts` lists the user's alerts newest first (`unacknowledgedOnly`, `limit` up to 500; the Owner
  may add `everyUser=true`), `GET /api/bots/{id}/alerts` one owned bot's, and `POST /api/alerts/{id}/acknowledge` marks
  one read. Another user's alert or bot answers 404. Telegram delivery follows in stage 5.15.

## Consequences

- Every control is enforced by the stores and aggregates the API and the Worker share, so a kill or halt takes effect
  even when no runtime holds the bot; the runtime of a killed bot ends on its next pass.
- Kill cancels are ordinary intents: they survive restarts and use the gateway's lookup on failure, and
  reconciliation applies their outcome when the bot opens again.

## Amendment: sells are bounded by the position, not by the per-order limit

- A sell closes a position that was bought under the limit, and it is limited by that position. The engine rejects a
  sell above the available base with `InsufficientBase` before an intent exists and reserves the sold quantity
  (`OrderSnapshot.Reservation`), so no sell can exceed what the bot holds. The per-order limit therefore applies to
  buys only.
- Valuing a sell at its limit price blocked exits: a ladder exit above the entry price could exceed the limit while the
  entry did not, so the bot kept a position with no way to close it (Steam, 2026-10-26: a 692.00 exit refused against a
  600 limit).
- `PerOrderLimit` holds the rule; the dispatcher no longer reads the last closed candle to value a sell.

## Amendment: sell price floor (ADR 0024)

- Because a sell is bounded by the position and not by the per-order limit, the one thing left to catch is a mistyped price
  that gives the position away. A limit sell priced below half of the last closed candle's close is rejected with
  `RiskLimit` before its first place; a market sell is not floored. The ratio is a constant, `0.5`; making it a bot
  setting is open (ADR 0024, question 2).
