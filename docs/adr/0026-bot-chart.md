# ADR 0026: Bot Chart

Status: Proposed, 2026-10-26. Stage 1 (closed candles) is built with this pull request. The owner delegated the decisions
below while away ("принимай решения сам") and reviews them in the pull request.

## Context

The bot page shows the asset, the position, the fills and the open orders, but no price history. The owner wants a candle
chart of the bot's own series that scrolls back through history, marks the bot's trades and later shows the forming
candle. What the code had before this decision:

- **Stored candles.** `candles` is keyed by `(venue, symbol, timeframe, open_time)` (migration `202610240005`), and
  `candle_closures` by `(venue, symbol, timeframe, from_time)`. The venue is part of both keys, so Bybit, T-Invest and Steam
  series never collide, even for equal symbols. `GetCandlesQuery` (`/api/historical-data/candles`) reads stored candles of a
  half-open UTC range with a limit and never calls a venue. `CandleDownloadService.Ensure` fills missing ranges from
  `IHistoricalCandleSource` in checked chunks, each stored in its own transaction; it stores a chunk only when candles and
  market closures cover every interval of it, and backtests rely on that.
- **Live candles are never stored.** The runner reads `ILiveCandleSource` directly (`BotRunner.Trade`); Bybit fills a
  stream gap from public REST in memory (`BybitLiveHistory`), Steam re-reads its median history (`SteamLiveCandleSource`).
  So the cache holds only what a download, a backtest's history preparation or a CSV import brought, and between the
  newest cached candle and now there is usually a hole.
- **Venues.** Bybit's history is public. T-Invest has no anonymous market data: history is read with the caller's credential
  (`IVenueMarketDataAccess`, ADR 0023, TC6), and nights and weekends are market closures. Steam reads through the configured
  market-data account at the lowest queue priority, reuses an item's history for `HistoryReuse`, offers hourly medians only
  for about the last 30 days and has no candle for an hour or day without sales, so its history is not contiguous and
  `Ensure` refuses it as incomplete.
- **Fills.** `bot_fills` is the bot's trading record (ADR 0021), paged newest first by sequence.

## Decision

1. **One endpoint, everything from the bot.** `GET /api/bots/{id}/candles?from&to` (`GetBotCandlesQuery`) takes only a
   half-open UTC range. The venue, symbol, timeframe and market data access come from the owner's bot; another user's bot
   is reported as missing. The range is widened to the bot's timeframe grid (from down, to up), cut at the latest closed
   candle, and may cover at most 1000 candles. The chart shows the bot's timeframe only; there is no switch.
2. **Read-through of one page.** Before reading, the handler asks `CandleDownloadService.Ensure` for the page's range with
   the bot's market data access (`BotMarketDataAccess`: the bot's credential for a venue with `IVenueMarketDataAccess`,
   none otherwise). A cached page calls no venue; a missing part is fetched once and stored like a download chunk, so the
   hole between history and live closes as the chart is opened. This is a bounded venue read in an HTTP request, like
   instrument search and rules; whole-history downloads stay background jobs (ADR 0011).
3. **Incomplete history is shown, not stored.** When `Ensure` reports intervals without a candle, the page shows the
   stored candles together with the venue's own candles of the range, marks the page `Partial` and stores nothing of it:
   the cache keeps only ranges the venue covers completely. A Steam hour or day without sales is therefore a missing bar,
   not an error. When the venue cannot be read, the page shows the stored candles and is `Unavailable` with the history
   error code.
4. **Fills by the same window.** The page carries the bot's fills of the same range (`IBotFillRepository.ListFilled`, at most
   1000, oldest first, `fillsTruncated` beyond that). Fills stay in `bot_fills`; nothing is copied into `candles`. The
   chart places each fill on the candle whose period contains it, with the arrow tip exactly at the fill's price (a buy
   arrow points up from below it, a sell arrow down from above it); a fill no drawn candle covers (a gap, or after the
   newest closed candle) is left out rather than pinned to a neighbour at the wrong height. Fills of one side on one candle
   keep their prices and share one label with their count. The chart draws the bot's working limit orders (`/open-orders`,
   refreshed with the page) as dashed levels titled with side, price and quantity.
5. **Transport.** Prices, volumes and fill amounts are exact decimal strings; times are UTC; the status is an enum name.
6. **Frontend.** The chart reuses lightweight-charts from `modules/market-chart` (`CandleHistoryChart`), which supports
   prepending data, series markers and price lines. `modules/bots` pages back 500 candles at a time when the view nears
   the oldest candle (or by a button), merges pages by open time, stops at a page without candles, and reads the candles
   closed since the newest page every minute; when those no longer fit into one request, it starts over from now.

## Storage

A stored candle costs about 150 to 200 bytes with its index: about 80 to 100 MB per instrument and year at 1m and 1.5 MB
at 1h. With tens of instruments this needs no separate store for the chart. The venue is already in the candle key, so
the expected risk of colliding venues does not exist. Narrowing rows (`instrument_id`, a `smallint` timeframe),
partitioning and TimescaleDB stay deferred until volume requires them. `bot_fills` has no index on `filled_at`; the
window query scans one bot's fills, which is small at current volumes and is the first index to add if it grows.

## Consequences

- Opening a bot's chart may call the venue once per uncached page. For T-Invest this shares the credential's request
  limits with the bot's own reads; the minute refresh reads only candles closed since the last page.
- A cached page is served from the database; a `Partial` page reads the venue on every request (Steam serves it from its
  reused history).
- The chart never shows the forming candle and never shows a fill before its candle has closed.
- `BotMarketDataAccess` is registered for the API as well as the Worker's runtime.

## Risks

- A venue may revise the newest closed candle shortly after its close (Steam closes a period `CandleCloseDelay` after its
  end). The chart, like a download, stores a candle as soon as its period ended. This predates the chart; a venue-neutral
  close delay for stored history is a possible follow-up.
- A page whose venue read fails stays `Unavailable` until the venue answers; there is no negative cache.

## Next Stages

Not part of this decision's first pull request:

1. **Forming candle.** A separate live channel per instrument, not the source the bot decides on: the bot still evaluates
   closed candles only. Each update carries an explicit closed or forming flag, lives in memory only and is never written
   to `candles`; the client updates by open time, throttled to one or two updates per second, one feed per instrument. On
   opening the chart: history first, then subscribe, then reconcile the overlap. The forming candle is drawn in a distinct
   style labelled «не закрыта»; signal markers appear only after the close. Bybit first, T-Invest second (one shared feed
   per instrument; request limits are shared with bots), Steam without a forming candle.
2. **Indicators and node conditions** in a tooltip or a panel under the chart.
