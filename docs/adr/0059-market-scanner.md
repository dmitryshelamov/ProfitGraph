# ADR 0059: Market Scanner

Status: Draft, 2026-10-29. The owner chose the shape in a design conversation (points marked "owner" below); the remaining
details are the analyst's proposals and are reviewed in the pull request. Nothing here is implemented. Builds on ADR 0003,
ADR 0010, ADR 0011, ADR 0027 and ADR 0042.

## Context

After a review of OS Engine the owner ranked new work: indicators, trend and counter-trend templates, a real grid, then a
scanner. "Screener" means two things:

1. **A scanner** searches the market and shows what it found: pairs that moved, volume far above usual, pairs suited to a
   strategy. A human decides what to do.
2. **A screener robot** (OS Engine's `BotTabScreener`) clones one strategy onto every instrument of a list, with a shared
   deposit and a shared limit of positions, and trades by itself.

The owner wants both, as separate products, and the scanner first ("сначала будем делать сканер"). The robot is not designed
here; the scanner must only not block it.

OS Engine has no standalone scanner. Its "Monitor" robots (`MonitorHighLow`, `MonitorImpulse`, `MonitorRsi`, `MonitorVolume`)
are screener robots with trading off by default: the user ticks instruments and one timeframe, each instrument gets a live
candle subscription through the connector, one hard-coded metric per robot is shown in a table with Chart/Open/Close buttons
and refreshed at most once a second, and an alert (sound and log) fires at most once per candle and instrument. A new filter
is a new robot. The terminal must stay open.

Bybit facts checked in its documentation on 2026-10-29:

- Public WebSockets need no keys. A spot subscribe request takes at most 10 topics; from 2026-11-26 a spot connection takes at
  most 100 topics; at most 500 new connections per 5 minutes per IP; ping every 20 s.
- The spot `tickers` topic (every 50 ms) carries 24-hour price, change, high, low, volume and turnover but no best bid or ask.
- REST `/v5/market/tickers?category=spot` returns `bid1Price` and `ask1Price` for spot pairs in one response.
- REST shares one IP limit of 600 requests per 5 seconds; exceeding it answers 403 and bans the IP for at least 10 minutes.

ProfitGraph already has the Bybit public stream transport (`IBybitPublicStreamTransport`), closed-candle and forming-candle
sources, history downloads as background jobs, backtests, comparison, and Telegram delivery (ADR 0042).

## Decision

### 1. What the scanner is

1. **A market scanner searches and shows; it never trades** (owner). Its result is a table of instruments with metrics, and,
   from step 2, Telegram alerts. A screener robot is a later, separate product.
2. **Bybit spot first** (owner). The universe is every spot pair quoted in USDT whose status is `Trading`; illiquid pairs are
   cut by a turnover filter on the page, not by the subscription. The universe is re-read from the instrument list every hour;
   new pairs are subscribed and delisted or halted pairs dropped.
3. **Venue capability.** Scanning is a venue port (`IMarketScanSource`, Application) that lists the scan universe with each
   instrument's listing time, backfills closed 1-minute candles over REST, streams closed 1-minute candles, and reads a quote
   snapshot (best bid and ask). `Venues.Bybit` implements it on the public REST and the existing public stream transport. A
   venue without the port has no scanner (ADR 0003); T-Invest (its market data stream) and Steam (slow polling only) are later
   candidates.

### 2. Where it runs and what it keeps

1. **Live, over WebSockets, in the Worker** (owner). A Worker role holds the subscription continuously and computes the
   metrics; the page and the alerts only read the result. One process holds it at a time (a PostgreSQL lease or advisory
   lock), so a second Worker does not double the connections.
2. **One topic per pair:** `kline.1.{symbol}`. Hourly candles are aggregated from the minutes in memory, the 24-hour figures
   come from candles, not from the ticker topic, so every value is reproducible from candles. About 500 pairs take 5
   connections of 100 topics.
3. **Warm-up and gaps.** At start the role reads 24 hours of 1-minute candles (2 requests per pair) and 7 days of hourly candles
   (1 request per pair) over REST, limited to 10 requests per second (about 2.5 minutes for 500 pairs); a 403 stops all REST
   calls for 10 minutes. A lost connection reconnects with backoff (as ADR 0044) and backfills the missed minutes over REST
   before the pair's metrics are shown again. A minute the venue reports without trades counts as a flat candle with zero
   volume; whether Bybit returns such candles is checked in the implementation.
4. **Memory, not the candle cache.** Scanner candles live only in the role's memory (about 500 × 1,440 minutes and 500 × 168
   hours). They are not written to the shared candle cache; a batch backtest (step 3) downloads its history through the
   ordinary history download.
5. **Snapshot.** After each minute closes, the role computes every pair's metrics and replaces the stored current snapshot (one
   row per venue and pair, with its computation time); spreads come from one REST quote snapshot per minute. No history of
   snapshots is kept. A `NOTIFY` tells the API, which pushes the snapshot to a SignalR hub `/ws/scanner`; the page shows the
   snapshot's age and reads the stored snapshot over HTTP when it opens.

### 3. Metrics

All windows are rolling and end at the last closed 1-minute candle; the forming candle is used only for the price. A metric
without enough history is empty, never computed on a shorter window. Metrics are pure functions of candle arrays, independent
of live and venue code, so the Worker, a backtest and a later screener robot compute the same value; prices and turnover stay
`decimal`, ratios may be `double`.

| Metric | Definition |
|---|---|
| Price | Last price of the current 1-minute candle. |
| Change 5m, 15m, 1h, 4h, 24h | (C_last − C_{t−N}) / C_{t−N}, %. |
| Turnover 1h, 24h | Sum of quote turnover of the last 60 and 1,440 minutes, USDT. |
| Volume spike | Turnover of the last hour / median hourly turnover of the previous 167 hours; empty below 72 hours of history. |
| Volatility (NATR 24h) | Simple mean of true range / close over the last 24 closed hourly candles, %. |
| Efficiency ratio 24h | \|C_t − C_{t−24}\| / Σ\|ΔC\| over hourly closes: 0 is a range, 1 a trend. |
| Distance to high and low, 24h and 7d | (C − High_N) / High_N and (C − Low_N) / Low_N, %. |
| Spread | (ask − bid) / mid from the minute's quote snapshot, %. |
| Listing age | Days since the pair's listing. |
| Stale | No trades for more than 15 minutes. |

RSI and other indicators are added when their engine nodes exist, computed by the engine's code. Order book depth, correlation
to BTC and cross-venue gaps are out of scope.

### 4. Presets

1. **Both scenarios are equal** (owner): catching a move now and picking pairs for a Ladder. The page offers two built-in presets:
   - **«Импульс»**: short-window change, volume spike, a turnover floor.
   - **«Под лесенку»**: low efficiency ratio, volatility above the round-trip fee, a turnover floor, a spread ceiling, and no
     pairs falling over 24 hours and 7 days.
2. A preset is a set of conditions (metric, minimum, maximum) and a sort. Every column filters and sorts. A user saves their own
   presets under a name; built-in presets are read-only and can be copied. Presets belong to the user; the snapshot is shared.
3. A row opens the pair's candle chart.

### 5. Telegram alerts (step 2)

1. **Alerts can be switched off** (owner). Each preset has its own alert switch, off by default, changed on the page at any time;
   each alert message has a «Отключить» button that switches that preset's alerts off.
2. A pair is reported when it enters a preset's result (it was not in it at the previous snapshot), at most once per pair and
   preset per cooldown (default one hour). Pairs that enter in the same snapshot go in one message per preset, at most ten
   named.
3. Delivery goes through the Telegram queue of ADR 0042 and needs a linked chat; without one the switch explains why it does
   nothing.

### 6. Batch backtest (step 3)

From the table or a preset's result the user picks pairs and runs one saved strategy version on each with the same period,
timeframe and capital. Missing history is downloaded by the ordinary download jobs, each pair is an ordinary deterministic
backtest run, and the batch shows overall progress and a ranking of its runs that can go to comparison. Its own details
(limits on pairs per batch, the ranking objective, what a failed pair does to the batch) are decided before step 3 begins.

### 7. Order of work

Three steps, each useful and checked on its own (owner): the live table, then alerts, then the batch backtest. See
[roadmap](../roadmap.md#market-scanner).

## Alternatives

- **On demand over REST.** A button recomputes the table. Nothing runs permanently, but every press costs hundreds of candle
  requests, the data is old a minute later, and alerts and the robot would need the live part anyway. Rejected by the owner in
  favour of WebSockets.
- **Subscription in the API while the page is open** (like the forming candle, ADR 0027). No permanent process, but alerts need
  the metrics without an open page, so it would move to the Worker at step 2.
- **One robot per metric, as OS Engine.** Each filter would be code; a common metric set with conditions lets the owner combine
  them without a release.
- **Ticker topic for the 24-hour figures.** Cheaper to read, but its values cannot be reproduced from candles and it has no bid
  or ask; the REST quote snapshot covers the spread.

## Consequences

- A new feature folder `Scanner` (Application, Infrastructure, Api, frontend module) is added to `docs/architecture.md` with the
  first implementation; the metric functions live with the engine's pure computation.
- The Worker holds about five permanent public connections and some tens of megabytes of candles; readiness reports the
  scanner role's state.
- Bybit's 100-topics-per-connection limit from 2026-11-26 is part of the design, not a later fix.
- A screener robot can later reuse the universe, the minute stream and the metric functions; the scanner does not depend on it.

## Open questions

- The default thresholds of the two built-in presets.
- Whether Bybit returns 1-minute candles for minutes without trades.
- The batch backtest details listed in section 6.
