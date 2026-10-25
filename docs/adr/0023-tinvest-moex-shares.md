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

## Amendment: live reads (T9b)

- **Live candles by polling, not the stream.** After each interval closes plus `Venues:TInvest:CandleCloseGrace` (20 s)
  the module reads the share's history up to the latest settled close with the history rules (reference share, candles
  without trades) and publishes its candles in order; closures are not published, the bot reads them from history at its
  next candle (ADR 0016, live market closures). An interval published without trades that later shows a real candle ends
  the feed with `HistoryDataCorrection`. Live decides a segment with what is known so far, so the first quiet minutes of
  a session can be closures live and candles without trades in a later backtest. Polling replaces the
  `MarketDataStream` subscription of the decision; each poll reads one padded day of the share and the reference, which a
  later change may narrow.
- **Commission rate.** `GetOrderPrice` for a limit buy at the last price (`GetLastPrices`) of at least
  `Venues:TInvest:CommissionProbeNotional` (100 000 RUB); the rate is deal commission over order amount, rounded to six
  decimal places, for limit and market orders alike.
- **Account value.** `GetPortfolio` in roubles: the portfolio total and each position by ticker; the available value is
  left unknown.
- **Display price.** None yet: `ILiveMarkPriceSource` runs without a bot's credential, so T-Invest registers none.

## Amendment: order gateway (T11)

- **Attempt first.** An attempt is written to `tinvest_order_attempts` as `Planned` before the runtime marks the place
  started, and moves to `Sent` before `PostOrder`. An order with no attempt, or with only a `Planned` one, never reached
  T-Invest: a `Planned` attempt is sent as it is, and a cancel of it is local. Every other attempt is resolved by
  `GetOrderState` with `ORDER_ID_TYPE_REQUEST`; an empty answer is not proof of absence, so the submit stays unresolved
  and is never sent again. The table lives in the module, with its migration (`202610250045`) beside it, like Steam's.
- **Request IDs.** A UUID version 8 from the first 16 bytes of SHA-256 over `profitgraph/tinvest-order-request/v1`, the
  bot GUID, the engine order and the attempt number.
- **Sizing.** A limit order and a market sell must be whole lots and a limit price must lie on the tick, as the share's
  rules require; otherwise the order is rejected `InvalidAmount` before anything is recorded. A market buy takes the whole
  lots its quote budget buys at the last price plus `Venues:TInvest:MarketBuySlippage` (1 %).
- **Updates.** `OrderStateStream` for the bot's account is only a signal: an order of the bot, known by its request ID,
  is read again with `GetOrderState` and translated. Executed base is executed lots times the lot; executed quote is the
  sum of the trade stages, each price per share times lots times the lot, so no average is trusted; the fee is the
  executed commission in roubles, and a fill whose commission is still zero is reported as pending. Stages that disagree
  with the executed lots, or another currency, are unresolved.
- **Rejections.** A status whose description is exactly a number rejects with the reason of Profit Fox Designer ADR
  0079's table and a fixed message with the code; the code is kept in the attempt, so a retried submit or a
  reconciliation reports the same rejection without sending. Until T12, `30098` and `30099` are broker rejections like any
  other unknown code.
- **Reconciliation.** The token must still have full access to the open account (`GetAccounts`), otherwise
  `KeyPermissions`. The order state stream is subscribed before the lookups. An active order on the bot's share whose
  request ID is not one of the bot's attempts is `ForeignOrder` unless the bot allows them; foreign trades are checked
  with the operations cursor in T13.

## Amendment: waiting for the session and the band (T12a)

- **Before every send** the gateway reads `GetTradingStatus`: API trading and the order type's flag must both be
  available, otherwise the attempt waits as `SessionClosed`. A limit attempt then needs the band from `GetOrderBook`
  at depth 1, kept for five seconds per share. A buy below `limit_down` or a sell above `limit_up` waits as `PriceBand`
  with the bounds. A buy above the band goes at `limit_up` rounded down to the tick, and a sell below it at `limit_down`
  rounded up, or it waits if that lands outside the band. A missing band waits. A market order is not checked against
  the band. The effective price is sent, not stored: the attempt keeps the engine's limit.
- **Waiting** is a `Planned` attempt with `waiting_code` and `band_low`/`band_high` (migration `202610250046`). The
  engine hears `LocalAccepted` when the attempt starts to wait; the runtime hears the reason (`RecordOrderWaitingCommand`)
  whenever it or the band changes. Reconciliation reports a waiting attempt again as `LocalAccepted`, and a cancel of it
  stays local.
- **Release.** `TInvestWaitingOrdersWorker` re-reads the status and band of waiting attempts every
  `Venues:TInvest:WaitingRecheckInterval` (30 s). It considers a bot only while its runner in this process reads updates,
  and only when the core's `AuthorizeWaitingReleaseCommand` allows it: the bot should run and is healthy, no trading
  halt, and a live lease with an open submit gate (ADR 0016 amendment). A stopped, killed or degraded bot never sends
  because a band moved. Sending ends the wait. A definitive rejection found then reaches the engine through the updates.
- **`30098` and `30099`** close the attempt with its code. The same engine order waits again as the next attempt with
  the next request ID (`SessionClosed` or `PriceBand`), and the engine hears no rejection.
- Still to come in T12b: carry-over of DAY orders before the last session ends, re-reading at the scheduled opening, and
  holiday intervals of today's schedule.

## Amendment: schedules and carry-over (T12b)

- **Schedule.** `TradingSchedules` for the share's own exchange (now kept on the share), from now through the next
  week, kept five minutes per exchange (thirty seconds after a failure). Intervals are classified with Profit Fox
  Designer ADR 0090's exact table (`tinvest-schedule/v1`):
  - Specific exchange tokens and `holiday_*` tokens are exchange time.
  - `dealer_*` tokens are never exchange time.
  - The generic `opening_auction`, `regular_trading_session` and `closing_auction` count only when a specific token
    covers exactly the same interval.
  - Any other token makes its day unknown.
  A day ends at the close of its last exchange interval, and the current trading day is the first day that has not
  ended. A schedule never authorizes a send. Today's `holiday_*` intervals count as closed even while the status is
  open; a schedule that cannot be read blocks nothing but carry-over.
- **Carry-over.** From `Venues:TInvest:CarryOverLead` (5 minutes, at most an hour) before the current day's end, the
  worker carries over each working DAY limit attempt of a reading bot:
  - It records `cancelled_by = CarryOver` and the trading date, then sends `CancelOrder` by request ID and reads the
    final state.
  - The engine order stays working, and fills found are reported cumulatively.
  - A cancelled attempt with whole lots left becomes the next attempt for exactly the remainder, waiting with
    `CarryOver` until the current trading day is later than the date it was carried from and the status allows it.
  - A fully filled attempt ends the order.
  - A carry-over that is not final yet is tried again at the next round.
- **Cumulative fills across attempts.** Each final attempt stores its own executed lots, quote and fee
  (migration `202610250047`), and an attempt's updates add those of the order's earlier final attempts. A rejection
  after fills of an earlier attempt is unresolved.
- **Who cancelled.** An owner or strategy cancel records `cancelled_by = Owner` before its request and wins over a
  carry-over in progress. Its cancellation ends the engine order, and a cancel of a waiting remainder stays local. A
  cancellation with no recorded reason, such as a DAY order the exchange expired, is `UnresolvedOrder`.
- **Unknown days.** A day with an unknown token, or no schedule, plans no carry-over. Its DAY orders expire at the
  exchange, so the bot degrades with `UnresolvedOrder`, which alerts the owner. There is no separate schedule alert.
- **Not modelled.** No separate read at the scheduled opening: the 30-second recheck covers it. Holiday weekday
  sessions without `holiday_*` tokens are not recognised.

## Amendment: late commissions (T13)

- **Source.** The order's own `executed_commission` from `GetOrderState`, not the operations cursor. It is a
  non-negative amount per order, while the sign of nonzero broker-fee operation amounts remains unconfirmed (Profit Fox
  Designer ADR 0076 discarded them in production for that reason). Re-reading the order is what Profit Fox did. The
  operations cursor, foreign trades found through operations, and a decrease alert stay out until an owner capture
  confirms the operation shapes; foreign *orders* are still refused at reconciliation and in the order stream.
- **Partitioned fills.** T-Invest's commission is cumulative per order, so a fee that grows after a known one belongs to
  the new executions. Every fill is published with `CommissionPartitioned`, and the engine keeps a slice's fee pending
  while an earlier slice's fee is.
- **Correction.** Every `Venues:TInvest:CommissionRecheckInterval` (5 minutes), the worker re-reads each final attempt
  with executions and a zero commission, for at most `CommissionPendingWindow` (7 days) after it became final, and only
  for bots whose runner reads updates.
  - A positive rouble commission is recorded on the attempt once.
  - It reaches the engine as one `FeeCorrection` of the order's slice ending with that attempt, cumulative over the
    order's earlier attempts. The runtime records it as a fee correction in the feed (TC4).
  - The engine refuses a correction while an earlier slice's fee is pending; no split is inferred.
  - A commission arriving after the window stays pending at the bot's rate.
- **Owner checks added.** The commission in `GetOrderState` must eventually become positive for a filled order, and a
  growing cumulative commission must not revise fees of executions already reported.
## Amendment: live frontend (T14)

- The bot creation form offers the timeframes of the chosen key's venue from its descriptor. For a venue that reads
  market data with the caller's key, it suggests shares found with that key (`TQBR:SBER`). Choosing a non-Bybit key
  clears the Bybit default symbol.
- A fill whose order's commission has not arrived yet shows «комиссия ещё не пришла» instead of its fee. The fills
  endpoint derives `commissionPending` from the fee receipts of the bot's current checkpoint, so fill rows stay
  immutable and a correction clears the mark. Waiting reasons (band, session, carry-over) and «Комиссия уточнена» in the
  feed came with TC4.

## Amendment: ladder scenario on the mock (T15)

- `TInvestLadderScenarioTests` runs a Ladder bot on an invented SBER (four-hour candles; the module has no daily
  interval) through the real T-Invest module and its waiting worker in a Worker host with the live role. Trading is
  enabled only in that test host, against the in-process Invest API mock with an invented token and account. The steps:
  - The entry's place answer is lost; it is found by its request ID and never sent again.
  - The averaging buy waits for the price band and is placed once the band reaches it.
  - The entry's late commission becomes one fee correction.
  - The exit and a partly filled averaging buy are carried over before the trading day ends.
  - A restart at night, with trading closed, sends nothing and keeps both carried remainders waiting.
  A closed session on submit is covered by the gateway's unit tests.
- **Core fix found by the scenario.** `BotMarketDataAccess.Open` set the ambient market data access inside an awaited
  method, and an `AsyncLocal` value set there does not flow back to the caller. The bot runtime's history, rules and
  live reads therefore never carried the bot's credential, and a T-Invest bot could not open. It now only reads the
  credential, and each caller opens the access with `MarketDataAccess.Use` in its own method. Bybit and Steam declare
  no market data access and are unaffected.
- Before trading, the owner checks listed above still apply. So do the checks added with T13, and the observation that
  the order state stream's messages carry `order_request_id`.

## Amendment: backtest milestone (T16)

- **Core fix found by the end-to-end run.** A backtest's history check (`BacktestConfiguration.CheckHistory`) required
  warm-up and replay counts to equal the grid intervals of their ranges. Market closures (TC1) make candles fewer than
  intervals, so every T-Invest backtest and optimization failed with `invalid_backtest_history` after preparing history.
  The engine replay itself was correct.
  - `BacktestHistoryInfo` now carries the closed intervals of each range, counted from the prepared history's
    closures. They are stored with runs and optimizations (migration `202610250048`, default 0), and the check counts
    candles plus closed intervals.
  - Bybit and Steam have no closures, so their values are 0 and nothing else changes.
- **The run says so.** `IVenueDescriptor.BacktestLimits` (default empty) names what a venue's backtests do not model as
  stable codes, and `GET /api/market-data/venues` returns them. T-Invest lists six: `reference_calendar`,
  `holiday_sessions`, `price_band`, `trading_status`, `gap_fills_at_limit` and `no_trades_fill_nothing`. The backtest
  setup form and the run's parameters card show them as fixed Russian texts, replacing the form's hard-coded T-Invest
  note.
- **Proof of the milestone** ("a Ladder backtest on minute candles of a MOEX share runs from share selection to result
  charts with closures, candles without trades, lots and rouble fees"):
  - `TInvestBacktestTests.ALadderBacktestOfAMoexShareRunsFromShareSelectionToItsResults` runs through the API and a job
    Worker with the real module against the mock. A key with its account is added, the share is found with the key, a
    Ladder backtest runs over a week of minutes and completes, and the results are read back. Nights and the weekend are
    closures, no fill lands on a candle without trades, every fill is whole lots, fees are in roubles, and the chart has
    points.
  - The frontend router test "starts a T-Invest run with the user's key and says what it approximates" covers the form.
  - The engine-level `TInvestMarketDataTests.ALadderReplaysAWeekOfMinutesDeterministically` remains.
