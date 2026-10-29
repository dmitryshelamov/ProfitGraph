# ADR 0062: T-Invest Market Scanner

Status: Proposed, 2026-10-30. The owner asked for the market scanner on T-Invest with everything Bybit has: the table of shares
with metrics, presets, the pair's chart, levels, alerts and trade cards. The details are proposals reviewed in the pull request.
Builds on ADR 0003, ADR 0023, ADR 0026, ADR 0059 and ADR 0060; amends ADR 0059 (section 3, volume spike) and makes T-Invest the
second scanner venue its section 1 names as a later candidate.

## Context

The scanner (ADR 0059) runs in the Worker's `scanner` role for every venue with an `IMarketScanSource`: a universe, REST history
of closed minutes and hours, a stream of 1-minute candles and a quote snapshot. Bybit Spot is the only source. T-Invest differs:

- **No anonymous market data.** Every Invest API call carries a token; reads of a use case open the caller's credential as market
  data access (ADR 0023, TC6). The Worker's scan has no caller, and every user's pair chart, levels and cards read history through
  the shared candle cache, which needs a T-Invest credential too.
- **Limits per token.** The official limits page, as Profit Fox Designer ADR 0074 records it: 32 market data streams per token,
  300 candle, order book and trade subscriptions per stream, 100 subscription changes per minute. Unary requests are limited per
  method group per minute; ProfitGraph's local ceilings are `Venues:TInvest:Limits` (`MarketData` 300).
- **A market that closes.** MOEX trades shares on weekdays from the morning session to the evening session; nights, weekends
  (weekend sessions are closures in history, ADR 0023) and holidays have no trades. The live stream sends nothing then.
- **Shares, lots, roubles.** Volume is in lots; candles carry no turnover; there is no request for the best bid and ask of many
  shares at once (`GetOrderBook` reads one share).
- **No closed flag** on a streamed candle, unless the subscription asks to wait for the interval's close (`waiting_close`).

## Decision

### 1. The scanner's credential

1. **A stored credential named in configuration** (`Venues:TInvest:Scanner:CredentialId`, production `.env`
   `TINVEST_SCANNER_CREDENTIAL_ID`), as Steam's market data credential (`Venues:Steam:MarketDataCredentialId`, ADR 0022). The
   owner adds a T-Invest credential on the credentials page, ideally with a read-only token of its own so the scanner never spends a
   trading token's limits, and puts its identifier into the API's and the `worker-scanner`'s environment. T-Invest has a scanner
   only while the venue is enabled and the identifier is set; otherwise the page does not list it.
2. **Decrypted through the application's credential path.** The venue module reads the credential's owner and venue, then sends
   `ReadExchangeCredentialSecretsQuery` for that owner, as the live gateways do, and keeps the decrypted token in memory for
   `CredentialReuse` (5 minutes), so a deleted or replaced credential stops being used within minutes. A missing credential, one of
   another venue or a missing master key make every scanner read fail with `market_scan_provider_unavailable`; nothing is sent.
3. **One port for every reader.** `IScanMarketDataAccess` (Application) gives a venue's scanner credential. The T-Invest source
   reads with its token; the scanner's chart (`GET /api/scanner/{venue}/candles`), levels and trade cards open it as market data
   access around their reads through the shared candle cache (`ScanMarketDataAccess`). Every user who opens the T-Invest scanner reads
   public market data with the owner's scanner credential, as every Steam history read uses the configured Steam account; the
   cards' bots still trade with the owner's own credential from the scanner trade settings (ADR 0060).
4. **Rejected:** a raw token in the environment bypasses the encrypted credential store; a credential chosen per user does not fit
   a snapshot shared by all users and has no caller in the Worker.

### 2. Universe, history and stream

1. **Universe.** `InstrumentsService/Shares` with `INSTRUMENT_STATUS_BASE`, read hourly like Bybit's list: main-board (`TQBR`)
   rouble shares whose API trading, buying and selling are available, with a positive lot and price increment. Shares only qualified
   investors may buy are left out unless `Scanner:QualifiedInvestor` is true, since the cards could not buy them. The current
   trading status does not filter: it changes with every session and would restart the streams. `ListedAt` is the share's IPO date.
2. **History.** `GetCandles` with the `Exchange` source (dealer and weekend candles never enter), in the windows of ADR 0023 (one day
   of minutes, 90 days of hours per request). Lots become shares; turnover is volume times the typical price (high + low + close) / 3,
   an approximation in roubles. Weekend candles are dropped, as in history. A candle of another source, off its grid or inconsistent is
   invalid provider data.
3. **Stream.** One `MarketDataStream` per `Scanner:SharesPerStream` shares (50), subscribed to 1-minute candles with `waiting_close`
   and the `Exchange` source in one request, so every streamed candle is closed. The stream asks the broker to ping every
   `Scanner:PingInterval` (30 s) and reports subscription and pings as `ScanCandleUpdateInternal.Alive`, so the runner's two-minute
   stall timeout does not reconnect a stream that is silent because the market is closed. A refused subscription ends the stream and
   the runner reconnects with its backoff. An update of another share, a weekend minute or an invalid candle is dropped.
4. **No spread.** `Quotes` returns nothing: order book subscriptions for every share would double the subscriptions and spend the
   change budget on every reconnect, and `GetOrderBook` per share each minute would take the whole request budget. The spread column
   stays empty on T-Invest; the built-in presets do not use it. The price is the last closed minute's close (no forming candle).

### 3. Budget

1. **Requests.** `Scanner:RequestsPerMinute` (120) unary requests a minute, spaced evenly, a share of the token's `MarketData` ceiling
   that leaves room for charts and bots on the same token. Warm-up of about 250 shares takes two requests each (a day of minutes and a
   week of hours): about four minutes. A reconnected stream backfills its 50 shares with up to 100 requests. A `RESOURCE_EXHAUSTED`
   answer pauses the scanner's requests for `RateLimitDefaultReset` (one minute) besides the transport's own pause.
2. **Subscriptions.** `Scanner:SubscriptionsPerMinute` (50) candle subscriptions a minute, half of the broker's 100 changes, spaced
   by the number of shares each stream subscribes; a stream waits for its turn before it connects, and its history is read only after
   its subscription is confirmed (ADR 0059, section 2, point 5). About 250 shares take five streams and five minutes to subscribe.
   Unsubscribing is not sent: a cancelled stream takes its subscriptions with it.
3. A preset backtest (ADR 0059, section 6) reads 38 days of minutes per share at one request per day: about 9,500 requests for 250
   shares, about 80 minutes at 120 requests a minute.

### 4. Metrics on a market with breaks

The scanner keeps calendar time on every venue: the series fills minutes without trades flat, nights and weekends included, and
every window is a span of time ending at the last closed minute (ADR 0059, section 3). Measured on that:

| Metric | On T-Invest | Decision |
|---|---|---|
| Stale | True from 15 minutes after the last trade: every share is stale at night and on weekends. Presets that hide stale pairs are empty then, and their alerts do not fire on windows that slide over a closed market. | Kept; the built-in presets hide stale pairs. |
| Change, turnover, distances 24h and 7d | A day ending on a weekday at noon spans one trading day; on Monday morning it spans the weekend and only Monday's first hours, so 24-hour turnover is low then and the change is the change since Friday. | Kept and documented; thresholds are set for a weekday session. |
| NATR and efficiency ratio 24h | The flat night hours add a true range of zero: NATR is about 17/24 of the mean over trading hours, much lower on Monday mornings. The opening gap counts in the first hour's true range. ER is a ratio of moves and changes little. | Kept and documented; the built-in NATR floor is lower than Bybit's. |
| Volume spike | The median of all 167 hours is at or near zero when about half of the week is closed (nights and weekends), so the spike was empty or huge. | **Changed for every venue:** the baseline is the hours with trades among the 167; empty below 72 such hours. A Bybit pair that trades every hour is unchanged; a thin pair now compares with its trading hours instead of an empty median. Shares that trade only in the main session (about 43 hours a week) have no spike. |
| Levels | Flat hours stay within the range of the hour before them; the session's last hour may become a swing high or low before the pause. | Kept. |
| Listing age | From the IPO date. | Kept. |

Measuring windows in trading time (the last 1,440 trading minutes) would match how bots see T-Invest (closures skipped, ADR 0023),
but needs a session-aware series, calculator and replay for every venue; it is left until the calendar windows prove misleading in
use.

### 5. Presets, currency and page

1. **Built-in presets for shares** (`TInvestScanPresets`), in roubles, hiding stale shares:
   - **«Импульс»:** change 1h ≥ 2%, volume spike ≥ 3, turnover 24h ≥ 30 million roubles, sorted by change 1h.
   - **«Под лесенку»:** ER 24h ≤ 0.3, NATR 24h ≥ 0.35% (a round trip costs 0.1% on the «Трейдер» tariff, 0.6% on «Инвестор»),
     turnover 24h ≥ 100 million roubles, change 24h ≥ −5%, distance to the 7-day high ≥ −15%, sorted by NATR; no spread condition.
2. **Quote currency.** `IMarketScanSource.QuoteAsset` (Bybit's scanner quote asset, `RUB` on T-Invest) reaches the page through
   `GET /api/scanner/venues`; the cards, the trade settings and the trades show it instead of a fixed `USDT`.
3. **Errors.** A scanner read that fails on the venue is `503` (`502` for invalid provider data) instead of a server error.
4. The trade cards, levels and alerts work as on Bybit. The cards size with `ScanTrades:FeeRate` (0.1% a side) for both venues; the
   bot buys whole lots, so its amount may be below the card's.

### 6. Production

`worker-scanner` gets `Venues__TInvest__Enabled`, the endpoint and `Venues__TInvest__Scanner__CredentialId`; the API gets the
credential identifier with the other runtime switches. Both stay off until `.env` sets `TINVEST_ENABLED=true` and
`TINVEST_SCANNER_CREDENTIAL_ID`.

## Consequences

- The scanner page offers T-Invest beside Bybit once configured; each venue has its own universe, snapshot and presets.
- A T-Invest scanner token holds up to six market data streams of the token's 32; charts and bots on the same token share the rest
  and the request limits.
- At night and on weekends the T-Invest snapshot keeps updating every minute with every share stale.
- The volume spike changes on Bybit for pairs with hours without trades; the replay and the live calculator change together.
- Nothing has been run against the real Invest API: the source is tested on the in-process mock. The broker's behaviour for
  `waiting_close`, the ping interval, the counting of subscription changes and the volume of `Shares` is from its documentation.

## Open questions

- A first live run with the owner's permission: subscription confirmation and closed candles in a session, silence and pings at
  night, the time to warm up, the requests spent.
- Spreads from order books for the shares a preset finds, if the spread proves needed on MOEX.
- Trading-time windows for venues with sessions.
