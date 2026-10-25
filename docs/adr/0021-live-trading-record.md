# ADR 0021: Live Trading Record

Status: Accepted, 2026-10-25.

## Context

A live bot's engine account knows its balances, cost basis and realized profit, but only its checkpoint, which holds
active orders. The owner needs a durable record of what the bot executed and how its positions went, written exactly
once even when the venue repeats a report, the bot restarts or a commit is rolled back.

## Decision

- **Fills.** When a root applies a venue fill, the commit records one `bot_fills` row: the change of the order's gross
  base, gross quote and fee since its previous fill, by the order's side and tag, at the time the root ran. A buy pays
  its fee in base, a sell in quote. The order's cumulative gross base after the fill identifies the row (unique per bot
  and order), so a repeated or replayed report of the same venue state records nothing. A report the engine ignored,
  which changes no balance, records nothing either.
- **Position cycles.** A cycle opens when a root makes the bot's position non-flat and closes when a root makes it flat
  (`bot_position_cycles`, at most one open per bot). Its realized profit is the engine's realized profit over the
  cycle. The engine places the opening at the candle the fill falls in, so a live session passes the fill's time.
- **One transaction.** Fills and cycle changes are written in the root's commit with its checkpoint and intents
  (`BotRootCommitter`); a fenced or failed commit leaves no fill, cycle or event behind.
- **Event feed.** The outbox (ADR 0004) turns the bot's Domain events into `bot_events` rows, once each and in stored
  order: start, stop, kill, degradation, recovery, runtime stop, restart, graph change or rejection, stale and
  recovered feed, unavailable venue, held submit, order placed, rejected or failed, fill, position opened or closed,
  and settings changes. An entry has a kind, short invariant details by name and the event's time; it never holds
  credentials or venue payloads. The Worker's `live` role removes entries older than 90 days every hour.
- **Reads.** `GET /api/bots/{id}/feed?before={sequence}&limit={n}` pages the feed newest first. `GET /api/bots/{id}/fills?before={sequence}&limit={n}` pages fills newest first (`next` continues);
  `GET /api/bots/{id}/position?cycles={n}` returns the position from the checkpoint (base and quote totals, cost basis,
  average cost, realized profit, opening time) with the recent cycles. Decimals are exact strings.

## Consequences

- Recording is a by-product of the commit the runtime already makes; no second write path exists.
- Domain events `BotFillRecorded`, `BotPositionOpened` and `BotPositionClosed` reach the outbox with the commit, so the
  feed shows them only when the commit happened.

## Amendment: display price (stage 5.13)

- **Source.** A venue's `ILiveMarkPriceSource` streams the public last-trade price of a symbol; Bybit's comes from the
  unauthenticated `tickers.{symbol}` Spot topic. It is for display only: the engine and replay never read it, so
  fingerprints and replay hashes do not change (stage 5 plan S5-19, Profit Fox Designer ADR 0088).
- **Publisher.** The Worker's `live` role keeps one subscription per instrument of the bots that should run and stores
  at most one observation per instrument a second in `live_mark_prices`. An older observation never replaces a newer
  one; a stored one signals `live_mark_price`. A lost subscription is opened again after five seconds.
- **Position.** `GET /api/bots/{id}/position` values the position at the display price: the stored ticker when it is
  later than the last candle's close and at most five minutes old, otherwise the last candle's close. The response says
  which (`markPriceSource`), when it was observed and the unrealized profit.
- **Hub.** `/ws/live-prices` lets a signed-in user subscribe to their own bots; each stored price is sent as
  `LivePrice` to the subscribers of its instrument. Another user's bot is refused as missing.
