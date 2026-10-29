# ADR 0059: Market Scanner

Status: Accepted, 2026-10-29. The owner chose the shape in a design conversation (points marked "owner" below) and accepted
the draft ("ок, давай разрабатывать"); the remaining details are the analyst's proposals. Implementation follows the roadmap. Builds on ADR 0003,
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

1. **Live, over WebSockets, in the Worker** (owner). A new Worker role `scanner`, next to `jobs` and `live` in `Worker:Roles`
   and in the same binary and image, holds the subscription continuously and computes the metrics; the page and the alerts
   only read the result. One process holds it at a time (a PostgreSQL lease or advisory lock), so a second Worker does not
   double the connections.
2. **A separate container in production** (owner). Locally and in tests one process runs every role, as today. In production
   `docker-compose.prod.yml` adds a `worker-scanner` service from the same image with `Worker__Roles=scanner`, and the main
   `worker` lists `jobs,live` explicitly. A crash, a memory leak, a stall on hundreds of sockets or a warm-up of the scanner
   then never touches the live bots.
3. **The Bybit IP limit is shared with the bots** (owner). Bybit's 600 requests per 5 seconds count all HTTP traffic of one IP
   to `api.bybit.com`, the bots' private order calls included, and a ban lasts at least 10 minutes. A separate container does
   not separate the IP: a scanner that gets banned stops every Bybit bot on the server for that time. So the scanner keeps its
   REST calls at 10 per second (a twelfth of the limit) and, at its first 403, stops all its REST calls for 10 minutes and
   reports the ban in readiness. WebSocket subscriptions do not count against this limit.
4. **One topic per pair:** `kline.1.{symbol}`. Hourly candles are aggregated from the minutes in memory, the 24-hour figures
   come from candles, not from the ticker topic, so every value is reproducible from candles. About 500 pairs take 5
   connections of 100 topics.
5. **Warm-up: subscription first, then history.** For each pair, in this order, so no minute closes unseen between the REST
   answer and the subscription:
   1. Subscribe to `kline.1.{symbol}` and buffer the candles that arrive.
   2. Read 24 hours of 1-minute candles (2 requests) and 7 days of hourly candles (1 request) over REST.
   3. Merge the history and the buffer by open time; where they overlap, the closed candle wins.
   4. Show the pair once its series has no gap; until then it has no row or its metrics are empty.

   At 10 REST requests per second (point 3) warm-up takes about 2.5 minutes for 500 pairs.
   After warm-up only the stream moves the series: a closed minute is appended and the minute older than 24 hours dropped; a
   closed hour, aggregated from its minutes, is appended to the 7-day series and the oldest hour dropped. The forming minute
   only sets the price.
6. **Reconnect.** A lost connection reconnects with backoff (as ADR 0044) and repeats steps 1–3 for the missed minutes; until the
   gap is closed the pair is marked as incomplete. A minute the venue reports without trades counts as a flat candle with zero
   volume; whether Bybit returns such candles is checked in the implementation.
7. **Memory, not the candle cache.** Scanner candles live only in the role's memory (about 500 × 1,440 minutes and 500 × 168
   hours, some tens of megabytes). They are not written to the shared candle cache; a batch backtest (step 3) downloads its
   history through the ordinary history download. A restart of the Worker loses them and warms up again; meanwhile the page
   shows the last stored snapshot with its age. Keeping candles on disk for a faster restart is left until restarts prove
   frequent.
8. **Snapshot.** After each minute closes, the role computes every pair's metrics and replaces the stored current snapshot (one
   row per venue and pair, with its computation time); spreads come from one REST quote snapshot per minute. No history of
   snapshots is kept. A `NOTIFY` tells the API, which pushes the snapshot to a SignalR hub `/ws/scanner`; the page shows the
   snapshot's age and reads the stored snapshot over HTTP when it opens.

### 3. Metrics

All windows are rolling and end at the last closed 1-minute candle; the forming candle is used only for the price. A metric
without enough history is empty, never computed on a shorter window. Metrics are pure functions of candle arrays, independent
of live and venue code, so the Worker, a backtest and a later screener robot compute the same value. Every metric is `decimal`,
as the engine's indicators are (ADR 0056); they live in the engine's `Scanning/` folder.

| Metric | Definition |
|---|---|
| Price | Last price of the current 1-minute candle. |
| Change 5m, 15m, 1h, 4h, 24h | (Close of the last minute − Open of the first) / Open of the first, over the last N closed minutes, %. |
| Turnover 1h, 24h | Sum of quote turnover of the last 60 and 1,440 minutes, USDT. |
| Volume spike | Turnover of the last hour / median hourly turnover of the previous 167 hours; empty below 72 hours of history. |
| Volatility (NATR 24h) | Simple mean of true range / close over the last 24 closed hourly candles, %. |
| Efficiency ratio 24h | \|C_t − C_{t−24}\| / Σ\|ΔC\| over hourly closes: 0 is a range, 1 a trend. |
| Distance to high and low, 24h and 7d | (C − High_N) / High_N and (C − Low_N) / Low_N, %. |
| Spread | (ask − bid) / mid from the minute's quote snapshot, %. |
| Listing age | Days since the pair's listing. |
| Stale | The last minute with volume ended more than 15 minutes ago, or there is none. |

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
- The `worker-scanner` container holds about five permanent public connections and some tens of megabytes of candles;
  readiness reports the scanner role's state and a REST ban.
- Bybit's 100-topics-per-connection limit from 2026-11-26 is part of the design, not a later fix.
- A screener robot can later reuse the universe, the minute stream and the metric functions; the scanner does not depend on it.

## Open questions

- The default thresholds of the two built-in presets.
- Whether Bybit returns 1-minute candles for minutes without trades.
- The batch backtest details listed in section 6.
