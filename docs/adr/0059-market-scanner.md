# ADR 0059: Market Scanner

Status: Accepted, 2026-10-29. The owner chose the shape in a design conversation (points marked "owner" below) and accepted
the draft ("ок, давай разрабатывать"); the remaining details are the analyst's proposals. Implementation follows the roadmap. Builds on ADR 0003,
ADR 0010, ADR 0011, ADR 0027 and ADR 0042; amends ADR 0042 (a Telegram notification may report a scanner preset instead of a bot).

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
2. **A separate container in production** (owner). The default roles stay `jobs,live`, so tests and an ordinary Worker never
   open the scanner's venue connections; the Development settings list all three roles for one local process. In production
   `docker-compose.prod.yml` adds a `worker-scanner` service from the same image with `Worker__Roles=scanner`, and the main
   `worker` lists `jobs,live` explicitly. A crash, a memory leak, a stall on hundreds of sockets or a warm-up of the scanner
   then never touches the live bots.
3. **The Bybit IP limit is shared with the bots** (owner). Bybit's 600 requests per 5 seconds count all HTTP traffic of one IP
   to `api.bybit.com`, the bots' private order calls included, and a ban lasts at least 10 minutes. A separate container does
   not separate the IP: a scanner that gets banned stops every Bybit bot on the server for that time. So the scanner keeps its
   REST calls at 10 per second (a twelfth of the limit) and, at its first 403, stops all its REST calls for 10 minutes and
   logs the ban; readiness turns Degraded when the stored snapshot is older than five minutes. WebSocket subscriptions do not
   count against this limit.
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
   gap is closed the pair is marked as incomplete. A minute the venue reports nowhere had no trades and is a flat candle with
   zero volume: a hole the REST history leaves, and a minute the stream skips while connected. A real candle that arrives later
   for a flat minute replaces it.
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
| Volume spike | Turnover of the last hour / median hourly turnover of the hours with trades among the previous 167 hours; empty below 72 such hours (amended by ADR 0062: nights and weekends of a market that closes must not pull the median to zero). |
| Volatility (NATR 24h) | Simple mean of true range / close over the last 24 closed hourly candles, %. |
| Efficiency ratio 24h | \|C_t − C_{t−24}\| / Σ\|ΔC\| over hourly closes: 0 is a range, 1 a trend. |
| Distance to high and low, 24h and 7d | (C − High_N) / High_N and (C − Low_N) / Low_N, %. |
| Spread | (ask − bid) / mid from the minute's quote snapshot, %. |
| Listing age | Days since the pair's listing: the venue's listing time, or the first candle when the 7-day history starts later than asked; empty for older pairs when the venue publishes no listing time (Bybit Spot). |
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
3. A row opens the pair's candle chart (`/scanner/{venue}/{symbol}`, timeframes 1m, 5m, 15m, 1h, 4h and 1d, 15m first) next to
   its metrics from the latest snapshot. The chart reads the venue's public history through the shared candle cache, as a bot's
   chart does (ADR 0026): `GET /api/scanner/{venue}/candles`, at most 1,000 candles a page, only for a venue with a scanner.

### 5. Telegram alerts (step 2)

1. **Alerts can be switched off** (owner). Each preset has its own alert switch, off by default, changed on the page at any time;
   each alert message has a «Отключить» button that switches that preset's alerts off.
2. A pair is reported when it enters a preset's result (it was not in it at the previous snapshot), at most once per pair and
   preset per cooldown (default one hour). Pairs that enter in the same snapshot go in one message per preset, at most ten
   named.
3. Delivery goes through the Telegram queue of ADR 0042 and needs a linked chat; without one the switch explains why it does
   nothing.
4. Implementation:
   - **Own presets only.** Built-in presets live in code and have no row to switch; the page asks to save one or a copy first.
   - **Watch.** A preset whose alerts are on has a `scan_alert_watches` row (`ScanAlertWatch`): the time of the last snapshot
     checked and the pairs it remembers (in the result, or reported and still in their one-hour cooldown). Switching alerts off
     deletes the row. State lives in the database, so a scanner restart neither repeats nor loses alerts.
   - **Checking.** `ReplaceScanSnapshot` stores the snapshot and checks every watch of the venue in the same transaction, filtering
     rows as the page does (`ScanPreset.Keeps`: bounds inclusive, an empty metric fails, stale pairs dropped when asked). A row whose
     candles are being repaired counts as outside every result. The first check after alerts are switched on only records the
     result, so switching on never reports the pairs already in it; a changed preset records its result again in the same way. A
     snapshot not newer than the last check changes nothing.
   - **Message.** One `ScanAlert` notification per preset and snapshot (`telegram_notifications.scan_preset_id`, no bot): the
     preset, the venue, how many pairs entered, and the first ten with the price, the preset's condition metrics and its sort
     metric, rounded as on the page. It is skipped when older than 15 minutes. The «Отключить» button (`scanoff:{presetId}`)
     switches the preset's alerts off for the chat's own user only and removes the button.

### 6. Preset backtest (step 3)

1. **What it answers** (owner, 2026-10-29): what the price did after a preset's signals, with no strategy. Running a saved
   strategy on the pairs a preset finds is left for later; the screener robot (a scenario that waits for a pullback and confirms an
   entry) builds on the rules this backtest proves.
2. **Signal.** The preset's conditions are checked at the end of every minute of the last 30 days (owner) on every pair of the
   venue's current universe; a pair signals when it enters the result, at most once an hour, as the alerts do, and the first minute
   only records the result. Conditions on the spread are not checked: history has no quotes. Pairs delisted before today are not
   in the universe, so the result leans towards pairs that survived.
3. **Outcome.** Entry at the next minute's open, no fees; the change to the closes 15 minutes, 1 hour, 4 hours and 24 hours later
   and the largest rise and drop within 24 hours (to the highs and lows). A horizon the history does not reach yet is empty. The
   summary gives per outcome the count, the share above zero, the mean and the median; the newest 2,000 signals are stored.
4. **Replay.** `Engine/Scanning/ScanReplay` computes the live metrics at the end of every minute in constant time (sliding sums,
   monotonic queues, per-hour medians and hourly NATR and ER), over the last 1,440 minutes and the last 168 hours aggregated from
   the minutes; tests prove it equal to `ScanMetricCalculator` on the same window. `ScanStudy` finds the signals and outcomes;
   both are deterministic.
5. **Job.** `POST /api/scanner/{venue}/backtests` stores a `scan_backtests` row with a copy of the conditions and queues a
   `scan_backtest` job (ADR 0011). The Worker reads each pair's minutes of the period and the eight days before it (the longest
   window) through the scanner's REST budget, eight pairs at a time, replays them and keeps nothing else: about 22,000 requests, 15
   minutes at the Worker's 25 requests a second in production. History is not cached, so each backtest reads it again. A pair
   whose history cannot be read is listed as failed; a REST pause after a ban fails the job.

### 8. Levels and trade scenarios (step 5a)

The owner wants entries, exits and stops from history at the moment the scanner finds a pair, and support, resistance and the
reward-to-risk ratio next to its metrics (2026-10-29). Levels are shown at once; whether trading them pays is measured by the preset
backtest, which replays the same levels without looking ahead.

1. **Levels.** From the closed hours the scanner holds (up to 168): swing highs and lows (an hour whose high is above the two hours
   before it and not below the two after it, and the mirror for lows) and the extremes of those hours. Prices within 0.5 % of each
   other merge into one level, their mean. `Engine/Scanning/ScanLevels` computes them; the live calculator and the replay call the
   same function, so both see the same levels.
2. **Metrics.** The nearest resistance is the lowest level more than 0.1 % above the price, the nearest support the highest level
   more than 0.1 % below it; each is shown with its distance in percent. Reward-to-risk is the distance to resistance over the
   distance to support, for a long position from the current price. All five are ordinary metrics: columns, conditions in presets
   (for example `RewardRisk ≥ 2`), alert lines; the pair's chart draws the two levels.
3. **Scenarios.** The backtest simulates four trades after every signal, on the minutes that follow it:
   - **Levels:** entry at the next minute's open, stop at the support, target at the resistance.
   - **Levels, R:R ≥ 2:** the same, only when the entry's reward-to-risk is at least 2.
   - **Target 2R:** entry at the next open, stop at the support, target two risks above the entry.
   - **Pullback:** a limit at half of the last hour's rise (from its lowest low to the signal's close), valid for 4 hours; stop at
     that lowest low, target 2R.

   A trade without a support below its entry (or a resistance above, where it is the target) is not taken. A trade exits at the stop,
   the target or, 24 hours after its entry, at that minute's close; when one minute touches both, the stop counts. The result is in
   percent and in R (the entry-to-stop distance) after an assumed 0.1 % fee on each side. Per scenario the backtest shows how many
   signals became trades, the share of profitable ones, mean and median R, total R, the longest losing streak and the mean time in
   a trade; every stored signal carries its four trades.
4. **Signals kept.** Up to 10,000 signals are stored, so the page can filter one pair's signals over the whole period.
5. **Levels on the chart's timeframe** (owner, 2026-10-30). A pair's page shows support, resistance, their distances and
   reward-to-risk for the timeframe its chart shows, to judge an entry on that timeframe. `GET /api/scanner/{venue}/levels`
   reads the pair's last 168 closed candles of the timeframe through the shared candle cache, applies `ScanLevels` to them and
   takes the price of the pair's snapshot row (the last close when the snapshot lacks the pair); the page reads it again every
   minute and draws the two levels, labelled with the timeframe. The scanner's metrics, presets, alerts and backtest stay on
   hours: a metric that followed whichever chart a viewer opened would make a shared snapshot and its replay disagree. The
   algorithm is the same on every timeframe, including the 0.5 % merge, which is coarse on minutes; a timeframe-aware merge
   is a later decision.

### 7. Order of work

Three steps, each useful and checked on its own (owner): the live table, then alerts, then the preset backtest. See
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

- The default thresholds of the two built-in presets; the preset backtest gives the numbers to tune them.
- A history cache for preset backtests, if running them often makes the 15-minute read a burden.
