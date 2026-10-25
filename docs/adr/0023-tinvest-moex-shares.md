# ADR 0023: T-Invest MOEX Shares

Status: Accepted, 2026-10-25. Wave 2 of the roadmap starts with this ADR; its code follows in later pull requests.

## Context

The roadmap's wave 2 adds `ProfitGraph.Venues.TInvest`: MOEX shares traded through the T-Invest Invest API by the same
engine and bot runtime as Bybit Spot and Steam. Profit Fox Designer built it in four days (ADRs 0070–0080, 0084, 0085,
0090; about 9 500 lines of T-Invest code, 10 600 of tests, plus shared engine and live code). It never traded and was
never rehearsed in the sandbox. Its first calendar model could not work, because `TradingSchedules` refuses past dates
(`30003`); every MOEX share reports the calendar `moex_mrng_evng_e_wknd_dlr`, which its live path still refuses; late
commissions were never applied in production because the operation fee sign was unconfirmed; and broker codes were never
read, although the status description carries the number.

The roadmap expected T-Invest to need no core changes. It does: ADR 0009 requires contiguous candles on the timeframe
grid, ADR 0010 forbids synthesized candles, and MOEX has nights, weekends and minutes without trades. This ADR names the
neutral core changes (TC1–TC6) and keeps everything specific to T-Invest in the module.

## Decision

### Instruments

- **Venue.** `tinvest`, display name "T-Invest (MOEX)", timeframes `1m`, `3m`, `5m`, `15m`, `30m`, `1h`, `2h`, `4h`.
  `1d` is added only after the owner confirms that daily candles open on the UTC day grid; nothing is shifted or
  resampled.
- **Symbol.** `{classCode}:{ticker}`, for example `TQBR:SBER`; the API accepts it as `instrument_id` (`SBER_TQBR`). The
  instrument UID is recorded in the rules snapshot. A renamed ticker is a new series.
- **Scope.** Shares (`share`) on the main MOEX board `TQBR`, currency `rub`, `api_trade_available_flag` set, positive
  lot and price increment. Funds, bonds, margin and short selling are out of scope.
- **Rules.** Quantity step and minimum quantity are the lot in shares; price tick is `min_price_increment` exactly;
  minimum notional 0; base precision 0, quote precision 2 (display only: prices and fees stay exact); buy and sell fees
  in the quote asset with exact quote fees (`BuyFeeAsset = Quote`, `ExactQuoteFees`). Quotations are converted from
  units and nanos without floating point; candle volume is converted from lots to shares.
- **Lots need no engine change.** A replay market buy sized in quote fills whole lots because the fill quantity rounds
  down to the step; a sell rounds down to whole lots; fees in roubles leave no fractional shares. In live the gateway
  turns a market buy budget into lots at the last price plus the bot's slippage and rejects less than one lot locally
  (`BelowMinQuantity`), as Steam prices a market buy from the lowest listing.

### History, closures and empty intervals

- **Core change TC1, market closures.** A historical candle source returns candles and the closed ranges of the
  requested range. The candle cache stores closures beside candles (`candle_closures`, keyed by series and range,
  written in the same transaction as the chunk). Preparing history requires every missing grid interval to be covered by
  a closure; the run freezes its closures; the history identity adds them under a separate marker only when there are
  any. Replay accepts a gap covered by a closure; warm-up and `barsHeld` count candles, not elapsed time. Bybit and
  Steam return no closures, so their bytes, fingerprints and pinned replay hashes do not change.
- **Core change TC2, candles without trades.** A candle may be marked as having no trades: open, high, low and close
  equal the last earlier real close and volume is zero. Indicators, warm-up and `OnCandleClosed` treat it as any candle;
  replay matches no market or limit order on it, and working orders wait for a real candle. The flag is a `candles`
  column. CSV import neither creates nor replaces such candles; a real candle replaces one only through an explicit
  correction that is logged. The history identity marks only flagged candles. Steam may use the same flag for hours
  without sales.
- **Past calendar from a reference share.** Schedules are not used for history at all. For a target series the module
  also reads real candles of a reference share (`TQBR:SBER` by default, `Venues:TInvest:History:ReferenceSymbol`) on the
  same timeframe and range. An interval is open when the reference or the target traded in it. A segment is a run of
  consecutive open intervals. In a segment where the target traded at least once, its empty intervals become candles
  without trades from the target's last earlier real close (which may lie before the segment); without an earlier close
  they are closures. A segment without any target trade is closed for the target (for example a share that does not
  trade in the evening session). Every interval that is not open is a closure. The policy is versioned
  (`tinvest-reference/v1`); the reference symbol and its candle digest enter the history identity.
- **Weekend sessions are closures.** In history, intervals that open on Saturday or Sunday in Moscow time (UTC+3) are
  closures and their candles are dropped, for target and reference alike. Weekday holiday sessions (about four days a
  year) replay as ordinary sessions; live treats them as closed (below). Dealer sessions never enter: every request and
  subscription uses the `Exchange` candle source and checks the returned source. Requests never send `limit` with that
  source (`30220`).
- **Reads.** `GetCandles` pages by the documented windows per interval. Instrument search uses `FindInstrument` and
  validates each candidate with `ShareBy`.

### Access and credentials

- **Core change TC5, choosing a venue account.** A venue may expose `IVenueAccountDirectory`: the accounts a set of
  not-yet-saved credential fields can use. `POST /api/exchange-credentials/accounts` lists them; `POST
  /api/exchange-credentials` accepts the fields the venue's `IVenueCredentialSchema` declares (`fields`), keeps the
  `apiKey`/`apiSecret` form for Bybit, and lets the venue validate before anything is encrypted. Secrets travel only in
  request bodies and are redacted everywhere.
- **T-Invest credential.** Fields `token` and `accountId`. The form sends the token, shows the accounts (`GetAccounts`:
  name, type, status, access, whether eligible) and saves the chosen one. Saving reads the accounts again and accepts
  only `OPEN` with `FULL_ACCESS`, so access revoked after the listing is not bypassed. One account is stored once (keyed
  HMAC alias). Reconciliation repeats the check; anything else is `KeyPermissions`. The owner chooses the account; on an
  account where Profit Fox Designer trades, its orders on a bot's share are `ForeignOrder`. There is no sandbox
  environment in this wave.
- **Core change TC6, market data with the caller's credential.** T-Invest has no anonymous API. History, instrument
  search and rules reads take an optional `MarketDataAccess`: the caller's credential of the same venue, decrypted only
  for the read. Download, backtest and optimization requests for such a venue carry its `credentialId`; the job request
  stores the identifier, never the run configuration, the fingerprint or the history identity. A bot reads with its own
  credential. A run prepared from a complete cache, and its resume, need no credential. Bybit and Steam ignore the
  access.

### Live market data and sessions

- **Core change TC3, live closures.** The live candle port reports closures, and the bot's engine session accepts a gap
  that the source declared closed. Bybit and Steam declare none and keep their gap error.
- **Live candles.** `MarketDataStream` candles with `waiting_close`, `Exchange` source. An interval without a candle is
  decided after its close plus `Venues:TInvest:Candles:EmptyIntervalGrace` (20 s) and a `GetCandles` read that still
  returns nothing: with trading status open it is a candle without trades, otherwise a closure. A later real candle for
  an interval published without trades is `HistoryDataCorrection`; history and past decisions are not rewritten.
- **Authority to send.** Only the current trading status (`GetTradingStatus`: API, limit and market flags) authorizes a
  submit, never a schedule. A closed status keeps the order `LocalAccepted` with the waiting code `SessionClosed`; the
  bot stays running; the status is read again every 30 s while orders wait, and at the scheduled opening. Today's
  schedule intervals of type `holiday_*` count as closed.
- **Schedule vocabulary.** The live calendar is the share's own exchange (`moex_mrng_evng_e_wknd_dlr` for every share
  seen), read for today and the next week only. Interval types are classified by an exact versioned table (Profit Fox
  Designer ADR 0090): ordinary exchange tokens, `holiday_*` tokens, `dealer_*` tokens that are never exchange time,
  generic tokens that only count when paired with a specific one. An unknown token plans no carry-over and raises an
  owner alert.

### Orders

| Engine order | T-Invest action |
|---|---|
| Limit buy or sell | `PostOrder` limit, `TIME_IN_FORCE_DAY`, quantity in lots, at the original price or the favourable band bound |
| Market buy | `PostOrder` market in lots from the quote budget at the last price plus slippage |
| Market sell | `PostOrder` market in lots |
| Cancel | `CancelOrder` by request ID; a cancel of an order still waiting locally is local |

- **Attempts.** The module's table `tinvest_order_attempts` (ADR 0006 amendment, venue-owned tables) records each
  attempt of an engine order before it is sent: request UUID (deterministic from bot, engine order and attempt number;
  T-Invest `order_id` is a UID of at most 36 characters), exchange order ID, state (`Planned`, `Sent`, `Active`,
  `Final`, `Unknown`), waiting code, effective price, executed lots and money, commission. `ClientOrderIdFor` keeps the
  `pg…` format; the table maps it to attempts. An unknown outcome is resolved only by `GetOrderState` with the request
  ID, never by sending again. Order updates come from `OrderStateStream`; cumulative lots are multiplied by the lot,
  trade quantities are shares.
- **Price band.** Before a limit submit the gateway reads `limit_down`/`limit_up` (`GetOrderBook`, depth 1, cached 5 s).
  A buy below the band or a sell above it waits `LocalAccepted` with code `PriceBand` and the bounds; a buy above is
  sent at the upper bound and a sell below at the lower, on the tick, never worse for the bot. The engine order keeps
  its price. A definitive `30099` returns the attempt to waiting without an engine rejection. Replay does not model the
  band.
- **Day orders and carry-over.** `Venues:TInvest:Sessions:CarryOverLead` (5 minutes) before the end of the share's last
  exchange session of the day, the gateway cancels its working DAY orders, waits for their final state and trades, and
  keeps the engine order working with code `CarryOver`. The remainder `(Q − F) / lot` whole lots is placed as the next
  attempt when the status allows trading again. Quantity, limit, tag and reservation do not change. A cancel the gateway
  did not request is `UnresolvedOrder` for the owner. A cancel by the strategy or the owner while the order waits is
  local.
- **Rejections.** A definitive rejection is a gRPC status whose description is exactly a number from Profit Fox Designer
  ADR 0079's table; `30098` and `30099` are waiting, other known codes map to rejection reasons, unknown ones to
  `BrokerRejected` with the code. Anything else is an unknown outcome. Text is never searched.
- **Waiting codes.** `PriceBand` (with the bounds), `SessionClosed` and `CarryOver` are neutral waiting codes of a
  `LocalAccepted` order, as ADR 0022 describes for Steam; core change TC4 carries them to the feed and the bot page, and
  texts are made at the boundary.
- **Ownership.** Own orders are those whose `order_request_id` is in the attempts table; another order or trade on the
  bot's share is `ForeignOrder` unless the bot allows foreign orders.

### Fees

- **Rates from the exchange.** T-Invest has no tariff-rate method. `IVenueFeeRateReader` asks `GetOrderPrice` (a
  calculation, nothing is placed) for a limit order of the bot's share at the current price with at least
  `Venues:TInvest:Commissions:ProbeNotional` (100 000 RUB) and takes `deal_commission / initial_order_amount` as both
  the limit and the market rate. The tariff name from `GetInfo` is shown. No answer or a zero amount is
  `fee_rates_unavailable`. A backtest takes its rates from the request; `IVenueExecutionDefaults` prefills 0.05 %.
- **Late commissions.** A fill applies at once. A positive `executed_commission` is its fee; zero on a filled order is
  pending, reserved at the bot's rate and shown as "комиссия ещё не пришла". Reconciliation reads
  `GetOperationsByCursor` for the account and share with a cursor in the module's table, links broker-fee operations to
  the order's trade by `parent_operation_id`, and applies an increase once as the engine's `FeeCorrection`. A decrease
  or a foreign operation raises an alert and keeps trading.
- **Core change TC4, fee corrections and waiting in the runtime.** The engine already supports `LocalAccepted` and
  `FeeCorrection`. The bot runtime passes both to it, the trading record (ADR 0021) stores a correction apart from the
  fill, and the feed shows waiting states and "Комиссия уточнена: было X, стало Y".

### Account and transport

- **Account.** `IAccountBalanceReader` reads `GetPortfolio` in RUB with the snapshot currency of ADR 0015's amendment;
  available cash is `max(0, money − blocked)`. Share positions are used as reported, without subtracting blocked again.
- **Transport.** Generated clients from the official contracts (package `Tinkoff.InvestApi 0.6.25`, or the official
  proto files where it lacks a method), no SDK facade, no automatic retry of a write. One gRPC channel per address; the
  Russian Trusted Root and Sub CA are trusted only in that channel with DER pins (Profit Fox Designer ADR 0085).
  Per-credential limits per method group with the `x-ratelimit-*` headers (`Instruments` 150 per minute, the others with
  headroom below the documentation); only reads retry. The token is sent only as call metadata; logs and errors carry no
  token, account identifier or broker text.
- **Switches.** `Venues:TInvest:Enabled` and `Live:Trading:Enabled` are false in every committed setting; the address is
  empty and must be HTTPS.

## Consequences

- Wave 2 changes the core in the places TC1–TC6 name; each lands in its own pull request with tests, and Bybit's and
  Steam's fingerprints, pinned replay hashes and gateway contract tests stay unchanged.
- A T-Invest backtest is an approximation: closures and empty intervals come from a reference share, weekday holiday
  sessions replay as ordinary sessions, the band and the trading status are not modelled, a limit crossed by an opening
  gap fills at its limit, and a candle without trades fills nothing. The run says so.
- Every test talks to an in-process mock of the Invest API with invented tokens, built from the shapes Profit Fox
  Designer read from the real API (including `30003` for past schedules and `30220` for `limit` with the exchange
  source). No agent sends a request to T-Invest or handles a real token.

## Owner checks before trading

On the intended account: that daily candles open on the UTC day grid; that `GetOrderPrice` returns `deal_commission` for
shares matching the tariff, equal for limit and market; the sign and delay of broker-fee operations (redacted capture);
that a rejection's status description is the number (`30079`, `30042`); that `OrderState.order_request_id` is returned
for own orders; when the exchange removes DAY orders after the evening session; the token's rights (`FULL_ACCESS`, no
withdrawals); and that the endpoint certificate is still issued by the Russian Trusted Sub CA, which expires on
2027-03-06.

## Amendment: position age across closures (T2b)

- "`barsHeld` counts candles" is implemented as elapsed grid time minus the time in the history's market closures. In
  replay a position's age then equals the open-market candles held (a candle without trades counts, a night does not);
  live fills that open a position inside a candle keep their elapsed-time rounding. Without closures the result is
  exactly the previous elapsed-grid value, so Bybit and Steam runs and every pinned hash are unchanged. Live bots pass
  their closures with TC3.

## Amendment: history, rules and search (T7)

- Every read uses the caller's T-Invest credential (TC6); without one, history is `historical_data_unavailable` and search
  or rules are `instrument_rules_invalid_request`. The share is read by ticker and class code (`ShareBy`) and reused for
  an hour; it must be a rouble share that API trading allows, with a positive lot and price increment.
- History reads the share's and the reference share's exchange candles one day around the requested range, so a segment
  crossing its edges is judged whole; when the share has no real candle in that day before the range, its last real close
  is searched in up to `Venues:TInvest:History:PriorCloseWindows` (14) earlier request windows. The candle source recorded
  with downloads is the policy name `tinvest-reference/v1`.
- The first candle of a series is the share's first minute candle date rounded up to the grid.
- Search lists main-board shares that API trading allows (`FindInstrument`) with base asset the ticker and quote `RUB`.
- Backtests take their fee rates from the request; T-Invest registers no execution defaults.
