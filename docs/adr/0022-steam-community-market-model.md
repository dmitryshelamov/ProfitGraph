# ADR 0022: Steam Community Market Model

Status: Accepted, 2026-10-25. Wave 3 of the roadmap starts with this ADR; its code follows in later pull requests.

## Context

The roadmap's wave 3 adds `ProfitGraph.Venues.Steam`: Steam Community Market items traded by the same engine and bot
runtime as Bybit Spot. Profit Fox Designer built Steam in four days (ADRs 0081–0083, 0086, 0087, 0089; about 16 000
lines of code, 14 000 of tests and 3 000 of frontend, 84 commits). Its design held, but it ran into the real market
only late: the mock's response shapes were invented, so parsers expected fields Steam does not send; a KZT wallet was
unsupported; positive fill evidence from market history arrived last; and request budgets starved history behind
order checks. ADR 0009 removed the Steam fee model and acquisition holds from the engine, to come back as neutral
policies with their own ADR. This is that ADR.

Steam's web endpoints are not an official trading API. Automated use can restrict the account; no request budget makes
it safe. ProfitGraph never conceals automation: one honest client identity, no proxies, no CAPTCHA solving.

## Decision

### Instruments

- **Venue.** `steam`, display name "Steam Community Market", timeframes `1h` and `1d`.
- **Symbol.** `{currency}:{appId}:{market_hash_name}`, for example `KZT:730:Dreams & Nightmares Case`. The item is the
  base asset (whole items) and the wallet currency the quote asset, as in a trading pair, so the candle cache key
  `(venue, symbol, timeframe, open_time)` separates histories in different wallet currencies without a schema change.
  The descriptor splits at the first two colons, writes the App ID canonically and keeps the hash name exactly: no
  trimming inside, no case folding, no Unicode normalization. The currency is one of a closed set with known minor
  units (USD, EUR, RUB, KZT at 0.01; only KZT is observed); any other is refused.
- **Rules.** Quantity step 1, minimum quantity 1, price tick = the currency's minor unit, minimum notional = the
  smallest representable buyer price. Base precision 0.
- **Core change C1.** `InstrumentSymbol` accepts single inner spaces and up to 256 characters (no leading or trailing
  whitespace, no control characters); the `symbol` columns widen to `varchar(256)`. Bybit's rule is unchanged.

### Market data

- **Candles are medians.** `/market/pricehistory` returns median price and sold count per hour (about the last 30
  days) and per day (older). A candle has `open = high = low = close = median` and volume in whole items. Inside the
  hourly zone a daily candle is the volume-weighted mean of complete hourly medians; the partial boundary day is a
  gap. Rules of Profit Fox Designer ADR 0081 ("Market-data model") apply unchanged, including the closing delay
  (120 s) and that a stored estimate is never replaced.
- **History needs a session.** Anonymous `pricehistory` answers 400, and prices come in the signed-in wallet's
  currency whatever the request says. The module reads history through one configured Steam account
  (`Venues:Steam:MarketDataCredentialId`); a symbol whose currency differs from that wallet's is
  `historical_data_unavailable`. Replay still requires contiguous candles: an hour without sales is a gap and fails
  the run, so only liquid items replay on `1h`. An empty-interval policy is left to a later ADR (T-Invest needs one).
- **Ports.** `IHistoricalCandleSource`, `IInstrumentCatalog` (market search), `IInstrumentRulesSource`,
  `ILiveCandleSource` (polls history after each close plus the delay; no stream exists) and `ILiveMarkPriceSource`
  (the item order book: lowest listing and highest buy order, polled, display only).

### Prices and fees

- **Listed price.** Every price in the engine is the buyer's price per item. A buyer pays exactly that, with no fee.
  The seller receives the listed price less two components, Steam's and the publisher's, each floored per item in
  minor units with a minimum of one minor unit (Profit Fox Designer ADR 0082 table and calculator).
- **Representable prices.** Not every buyer price is reachable from a whole seller amount. A sell limit price that is
  not representable is raised to the next representable price at or above it, never lowered, in replay and live alike;
  the order carries the raised price.
- **Core change C2, engine fee model.** Fees are either the limit and market rates (today's behavior, the default) or a
  venue-neutral `SellerFeeSchedule` on `ExecutionSettings` (component rates and a minimum; per unit, seller-paid,
  floored in minor units of the quote asset, with representable-price normalization), in which case both rates are
  zero. A stored configuration without a schedule keeps its shape, so Bybit fingerprints and pinned replay hashes stay
  byte-identical. Replay, sizing and the live account use the same calculator; the schedule assumes a price tick equal
  to the quote asset's minor unit.
- **Core change C3, fee model of a bot.** `BotFeeRates` (maker, taker) may carry a seller fee schedule instead;
  `IVenueFeeRateReader` returns either. Bybit returns `Proportional`. Steam reads `wallet_fee_percent`, `wallet_publisher_fee_percent_default`
  and the minimum from the wallet page with the bot's credential; missing evidence refuses the bot.
- **Core change C4, venue execution defaults.** A new optional port `IVenueExecutionDefaults` gives the default fee
  model, settlement hold and whole-item sizing of a venue for a new backtest; `GET /api/market-data/venues` returns
  them and the backtest form starts from them. The backtest request carries the fee model and hold explicitly, so a
  run stays reproducible when defaults change.

### Holding periods

- **Core change C5, settlement hold in replay.** `ExecutionSettings` gets an optional `SettlementHold` duration. A
  replay buy fill creates a lot sellable at the fill's market time plus the hold. Market time is the replay's explicit
  time: a candle's open time at its open, its close time at any later point of the candle. A sell larger than the unlocked
  quantity fills partly from the unlocked lots once its price is reached; the remainder stays working and is never
  reduced. Each partial fill is its own ledger entry, and the order's terminal `Filled` event is emitted once, after the
  whole quantity filled. Replay thereby gains partial limit fills, which only a settlement hold produces; without one a
  limit order still fills whole. Lots live in the replay state only; the live account has no lots. A backtest request
  takes the hold as `settlementHoldSeconds` (0 to 315 360 000); Steam's default is seven days, an estimate the run can
  change.
- **Live holds belong to the gateway.** The live engine does not track lots. The Steam gateway knows each owned
  asset's actual unlock time from the inventory (`Tradable/Marketable After`; an unreadable date is locked and is read
  again after 15 minutes). A sell whose assets are still locked is acknowledged with `LocalAccepted` and a waiting
  code; it is listed item by item as assets unlock, and its partial fills are real.

### Orders, confirmations and the bot model

| Engine order | Steam action |
|---|---|
| Limit buy | Buy order (`createbuyorder`) at the price for the quantity. At most one buy order per item per account: a further buy waits with `LocalAccepted` and a waiting code, the one nearest the market first, and is placed when the slot frees. Replay does not model the slot. |
| Market buy | Buy order at the lowest listing plus the bot's market-buy slippage, rounded up to the tick; the remainder is cancelled after the bot's deadline (2 minutes by default) and reported as cancelled. Partial fills are real. |
| Limit sell | One listing per owned, unlocked asset at the seller amount of the (raised) price, then a mobile confirmation. |
| Market sell | A listing at the highest buy-order price, so it sells at once, then a mobile confirmation. |
| Cancel | `cancelbuyorder` or `removelisting`; a cancel of an order still waiting locally is local. |

- **Intents stay the outbox.** `OrderIntent` (ADR 0016) still carries every submit and cancel, with `PlaceStartedAt` and
  lookup before any retry. `IOrderGateway.ClientOrderIdFor` returns the `pg…` identifier, which the Steam journal maps
  to Steam's own IDs (buy order ID, listing IDs, confirmation creator). A write that may have left the process is never
  posted again until reconciliation proves its outcome; an HTTP 429, an empty body or a timeout after a write is
  `Unknown`, not a rejection.
- **Mobile confirmations are part of placing.** A listing, and a buy that Steam answers with `need_confirmation`, are
  confirmed by the module's confirmation worker: signed `mobileconf/getlist` with server-aligned time, approval only of
  the confirmation whose type and creator match a journal entry, backoff 3 s to 2 min. Unmatched confirmations are never
  touched. The engine sees `Accepted` once the listing or buy order is active; an order still awaiting confirmation after
  15 minutes raises an alert and keeps waiting. Owner-selected confirmations of foreign listings are not included.
- **Fills need positive evidence.** A buy fill comes from the buy order's counters and a matching purchase in market
  history; a sale and its price come only from a sale event in `myhistory/render` (Profit Fox Designer ADR 0087: cursor,
  overlap, write-once attribution). A missing listing or asset never proves a fill or a cancel. A returned item with a
  new asset ID is a cancel only with positive correlation.
- **Ownership.** Only assets attributed to the bot's own buys may be listed; reservations are unique per asset. A
  foreign buy order or listing on the bot's item is `ForeignOrder` unless the bot allows foreign orders.
- **Reconciliation.** `Reconcile` reads the account snapshot (wallet, buy orders, listings, inventory, history since the
  cursor) and answers as ADR 0016 requires: updates of found orders, final states, and a safety reason. A credential
  whose Guard state is not `Ready`, a wallet currency other than the symbol's, or an open account breaker answers
  `KeyPermissions`.

### Account, session and request budget

- **Credentials, core change C6.** `ExchangeCredential` stores named encrypted fields that the venue declares
  (`IVenueCredentialSchema`; Bybit: `apiKey`, `apiSecret`). Steam's fields (Steam ID, refresh token, shared secret,
  identity secret, device ID) enter in one of two ways, as in Profit Fox Designer: a one-time offline console command
  run by the owner with the authenticator file on stdin (`--run steam-import` on the Worker; there is no web import), or enrollment of a new authenticator
  on the web (`start`, `recovery-saved`, `activate`): password sign-in with the email code, the recovery code shown once
  and never stored, email confirmation only, the pending session encrypted with a ten-minute TTL, then `CoolingDown`
  with a 15-day estimate until a real eligibility check says `Ready` or `Restricted`. The Owner can show the current
  Steam Guard code of an active or imported authenticator on request; it is generated on demand and never stored,
  logged or sent to Telegram. No Steam password is stored. The same Steam account cannot be stored twice (keyed HMAC
  alias, no plaintext Steam ID).
- **Session.** One session per credential owns login finalization, cookies and the session ID; renewal is single-flight;
  a 401, a login redirect or `needauth` renews once for a read and never replays a write. Server time is measured at start
  and hourly. Guard states `Checking`, `CoolingDown`, `Ready`, `Restricted` follow Profit Fox Designer ADR 0083 with its
  amendments, including the automatic recheck of `Checking`.
- **Scheduler.** One request in flight per account; priority confirmation and order writes, status, inventory and
  wallet, quotes, history; history only when nothing else waits. Cooldowns wait outside the gate, one retry layer for
  reads, no retry of a 429 or a write, write spacing 5 s, a pause of a waiting order's checks so history is not starved.
  Every number is a configurable, unconfirmed local policy.
- **Account protection.** CAPTCHA, restrictions, family view, repeated 401/403/429 and a high error ratio open a persisted
  breaker that degrades every Steam bot of the account; transient reasons resume automatically at most three times in six
  hours (Profit Fox Designer ADR 0089), hard reasons wait for the Owner. **Core change C7:** a neutral port
  `IVenueAccountProtection` (status, reason code, resume) behind `GET` and `POST /api/exchange-credentials/{id}/protection`,
  and the safety reason `VenueAccountProtection`. The global halt and per-bot kill of ADR 0018 apply as for Bybit; there
  are no separate Steam kill flags.

### Module boundaries

- **Core change C8, venue-owned tables.** `Venues.Steam` owns its tables (`steam_accounts`, `steam_account_protection`,
  `steam_order_journal`, `steam_asset_reservations`, `steam_market_receipts`, `steam_market_attributions`). Its migrations
  live in the module and join the shared runner; the module writes through the registered `NpgsqlDataSource` in short
  statements of its own, without referencing Infrastructure (ADR 0006 amendment).
- **Core change C9, venue bot settings.** The bot stores the module's typed, versioned settings (ADR 0003) as JSONB without
  interpreting them. Steam's: settlement-hold estimate (display), market-buy slippage and market-buy deadline.
- **Waiting codes.** A `LocalAccepted` order may carry a neutral waiting code (`SettlementHold` with the unlock time,
  `SingleBuyOrderSlot`, `AwaitingConfirmation`, `RateLimited`) that the feed and the bot page show; texts are made at the
  boundary.
- **One system per account.** Steam bots of ProfitGraph start on an account only after Profit Fox Designer's Steam bots
  on it are stopped; that is the owner's switch-over step. Designer's orders on a bot's item remain `ForeignOrder`.
- **Switches.** `Venues:Steam:Enabled` (any Steam request, also market data) and `Live:Trading:Enabled` (the gateway) are
  false in every committed setting. Steam endpoints are empty by default and require HTTPS on the official hosts.

## Consequences

- Wave 3 changes the core in the places C1–C9 name; each lands in its own pull request with tests, and Bybit's
  fingerprints, pinned replay hashes and gateway contract tests stay unchanged.
- A Steam backtest is an approximation: medians are not trades, there is no intrabar path, the replay hold is an
  estimate, the single buy slot and confirmation delays are not simulated, and a market buy is a timed limit order. The
  run says so.
- Every test talks to an in-process mock Steam with invented values. Response shapes come from what Profit Fox Designer
  read from the real market after its fixes, not from guesses; anything still unobserved stays disabled. No agent sends a
  request to Steam or handles a real cookie, token or Guard secret.

## Owner checks before trading

On the intended account, before enabling trading: the wallet currency and fee fields; a buy order fill, a sale and a
listing cancel in `myhistory/render` (redacted capture, Profit Fox Designer ADR 0087); that a buy order may need a
mobile confirmation; actual unlock times of bought items; and the account's market eligibility.
