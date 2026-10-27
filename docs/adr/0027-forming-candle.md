# ADR 0027: Forming Candle on the Bot Chart

Status: Proposed, 2026-10-26. Stage 2 of the bot chart ([ADR 0026](0026-bot-chart.md)). The owner decided the shape while
away ("формирующаяся свеча идёт отдельным каналом, хранится только в памяти"); the details below are the implementer's and
are reviewed in the pull request.

## Context

The chart of ADR 0026 shows closed candles only. The owner wants the candle that is still being formed to move in real time.
What exists before this decision:

- `ILiveCandleSource` delivers **closed** candles to bots in the Worker, in order and without gaps (ADR 0016). Bybit reads the
  public kline topic and keeps only `confirm=true`; T-Invest polls history after each interval closes (ADR 0023, T9b); Steam
  re-reads its median history. None of them is a stream of the forming candle, and a bot must keep deciding on closed
  candles.
- `/ws/live-prices` (ADR 0021) is a SignalR hub of the API process; the Worker stores a price and signals it through
  PostgreSQL. A forming candle updates about once a second per series, which does not belong in the database.
- The API already references every venue module and has the bot's market data credential (`BotMarketDataAccess`), so it can
  open a public Bybit topic or a T-Invest stream itself.
- T-Invest has no anonymous market data. Its market data stream is one bidirectional gRPC call per token that carries
  subscriptions as requests, and the broker limits the streams per token. Candle updates carry no close flag.

## Decision

1. **A separate port and channel.** `IFormingCandleSource` (Application, a venue port like `ILiveCandleSource`) streams
   `FormingCandleInternal` (open time, OHLC, volume, `IsClosed`, observation time) for a symbol and timeframe, ends or throws
   when the connection is lost, and says whether it supports a timeframe. It is not the bot's source: nothing the bot decides on
   comes from it, nothing is written to `candles`, and backtests and strategies never see it. A venue without the port (Steam)
   has no forming candle; a timeframe the venue cannot stream is the same.
2. **Feeds live in the API process.** `IFormingCandleFeeds` (implemented in Infrastructure, registered by the API only) keeps one
   upstream subscription per `(venue, symbol, timeframe, credential)` however many clients follow it, starts it with the first
   client and stops it when the last client leaves or disconnects. The credential is part of the key for a venue that reads with
   the caller's credential (the key of `MarketDataAccess`), so one owner's token never serves another owner's chart. The feed
   opens `MarketDataAccess` around the subscription, exactly as history reads do.
3. **Pacing and recovery.** A feed passes on at most one update per second, except a closed candle and the first candle of a new
   interval, which go at once; the newest candle held back goes out on the next tick. A lost connection or a feed silent for
   90 s is reported as `NoData`; the feed reconnects after 1 s, doubling to 1 min, and the next candle reports `Live`. Updates
   older than the last one published are dropped.
4. **Delivery over the existing realtime mechanism.** A new SignalR hub `/ws/forming-candles` next to the others: `Subscribe(botId)`
   joins the caller to its own bot's feed (another user's bot is `bot_not_found`) and returns the feed's state and latest
   candle; `Unsubscribe` and disconnecting leave it. Messages are `FormingCandle` and `FormingStatus` to the feed's group;
   prices and volume are exact decimal strings and times are UTC. A connection follows one bot at a time. The hub is the only
   new transport; no second socket and no polling.
5. **Bybit.** The public Spot kline topic of the closed candles is read again by `BybitFormingCandleSource`, which keeps every
   update and takes Bybit's `confirm` as `IsClosed`. The topic is public; the connection is never authenticated.
6. **T-Invest.** `TInvestCandleStreams` holds one `MarketDataStreamService/MarketDataStream` connection per token, shared by all
   candle subscriptions of that token (subscribe and unsubscribe are requests on it), and closes it with its last subscription,
   so a chart per share does not spend the broker's stream limit. The call goes through `TInvestTransport.Duplex`, which admits
   the opening and every request under the token's `MarketData` limit. Volume in lots becomes shares as in history; an update
   that is not a valid exchange candle of the timeframe is dropped. Every update has `IsClosed = false`; the next open time ends
   the candle.
7. **Frontend.** `useFormingCandle` follows the hub for the page's bot and keeps the current candle and the one before it. The
   chart draws them after the closed history with `series.update`; the candle that is not closed is drawn see-through and the
   page says «не закрыта». A live candle whose open time is at or before the history's newest candle is not drawn: the history
   stays authoritative, so a closed candle is never doubled and the previous candle stays on the chart until the minute refresh
   brings it from history. The page shows the state of the channel (connecting, live, no data); a lost link never affects the
   closed candles.

## Consequences

- A chart page holds one SignalR connection and, at most, one venue connection per series (and credential) in the API process.
  With several API instances each holds its own upstream; there is no cross-instance sharing.
- T-Invest forming candles need a credential with market data access and respect the shared per-token limits: the stream and its
  subscriptions are admitted by the same limiter as the bot's own reads.
- The last update of an interval from T-Invest may be up to a second old when the next interval starts; the closed history
  replaces it within a minute.
- Signal markers still appear only after the candle closes (ADR 0026).

## Risks

- The T-Invest stream and its message shapes were implemented against the SDK types and an in-process mock; they have not been
  run against the real broker (no token is used by agents). Whether a stream per token or a candle subscription per stream
  hits a broker limit that the code does not know is unverified; refusals end the subscription, and the feed backs off.
- A user can open many bots of many series; every distinct series costs one upstream connection while a page is open. A cap per
  user is deferred until it is needed.
- Between joining a feed and entering the hub group a single update can be missed; the snapshot carries the latest candle and the
  next update follows within a second.
