# ADR 0016: Live Bot Runtime

Status: Accepted, 2026-10-25.

## Context

Stage 5 runs strategies as live bots on Bybit Spot with the same engine as backtests. Profit Fox Designer ADRs 0054–0057
and 0091 defined the runtime: a bot with a desired state and a runtime status, one lease per bot with a fencing epoch,
checkpoints and order intents saved in one transaction, automatic restarts, kill switches and manual orders. Their
rules lived in SQL of a 1,200-line store (ADR 0004). ProfitGraph keeps the behavior and moves every rule into Domain
aggregates in feature `Bots`. This ADR records the aggregates (stage 5.2); persistence, the Worker runtime and the
gateway amend it in later stage 5 pull requests.

## Decision

- **Bot.** A bot belongs to its owner and runs one strategy on one candle series (`CandleSeries`: venue, symbol,
  timeframe) through one of the owner's credentials. It stores its parameter values, allocation cap, risk limits, fee
  rates read from the venue, the foreign-orders policy (stage 5 plan S5-16), the strategy version it runs, and whether
  a run exists. A new bot is stopped and has never run.
- **Desired state and runtime status.** The owner sets the desired state (`Stopped`, `Running`); the runtime that holds
  the lease reports `Created`, `Starting`, `Running`, `Degraded` or `Stopped`. `Start` is refused while trading is
  halted (`trading_halted`), runs the latest strategy version when no checkpoint exists, and otherwise keeps the stored
  version for a graph change at runtime. Starting a bot that should already run changes nothing, except that it
  re-arms a bot degraded by `IntentFailed` or `InsufficientBalance`. A start clears reasons, diagnostics, the restart
  streak and the daily loss baseline. `Stop` changes only the desired state; open orders stay.
- **Reasons.** `BotStatusReason` codes are classified in `BotStatusReasons`. `RuntimeFailure` restarts automatically
  after 30 s, 1, 2, 5 and 10 minutes; after five restarts the bot waits for the owner. Safety reasons (`NoGateway`,
  `UnknownVenue`, `IntentFailed`, `UnresolvedOrder`, `ForeignOrder`, `KeyPermissions`, `UnsupportedFeeAsset`,
  `InsufficientBalance`, `HistoryDataCorrection`) never clear themselves and are not replaced by a runtime failure.
  Control reasons (`Killed`, `DailyLossLimit`) come from a kill: desired and runtime state become stopped at once and
  the reason stays until the next start. Texts for people are produced from the codes at the boundary.
- **Leases.** `BotLease` gives one runtime process the right to execute a bot. The first lease has epoch 1; an expired
  lease is taken over under the next epoch with a closed submit gate. Only the holder of the current epoch renews,
  releases or opens the gate, and only before expiry. A bot may be leased while it should run and is not degraded, or
  when its runtime failure's restart time has come; leasing a degraded bot counts a restart. Only a root saved after a
  live closed candle resets the restart streak.
- **Order intents.** `OrderIntent` is the order outbox: a submit carries the engine's `OrderSnapshot`, a cancel only the
  engine order ID, and both carry the venue's client order ID. Claiming moves `Pending` to `Sending`; a claim older than
  30 seconds can be taken again. Before a place request leaves the process the intent records `PlaceStartedAt`; from
  then on every retry looks the order up first. Failures retry after 1, 2, 5, 15, 30 and then 60 seconds; the tenth
  failure fails the intent. A transient retry (an unreadable balance) waits five seconds without counting. A kill
  rejects every submit that never left the process with `KillSwitch`; cancels and possibly placed submits continue.
  Reconciliation resolves an undelivered intent once the venue state is known. The owner may confirm a submit absent
  at the venue (Profit Fox Designer ADR 0055 with its 2026-10-18 follow-ups) when its delivery stopped the bot
  (`IntentFailed` or `UnresolvedOrder`), it may have left the process, its engine order is still `PendingSubmit` with
  nothing filled, and its venue's descriptor says `ConfirmsOrderAbsence`. The intent records who and when, is not
  delivered again, and the next reconciliation passes the confirmation to the gateway, which looks the order up first
  and rejects it with `OwnerConfirmedAbsent` only when the venue shows nothing; otherwise it stays unresolved.
  `GET /api/bots/{id}/unresolved-orders` lists such submits and `POST .../order-intents/{intentId}/confirm-absent`
  confirms one; a restart of the bot then reconciles.
- **Daily loss.** The first live candle root of a UTC day stores the equity baseline; a later root whose loss from it
  reaches `DailyLossLimitQuote` kills the bot with `DailyLossLimit`.
- **Global halt.** `TradingHalt` is a singleton: halting raises `TradingHalted`, after which the caller kills the bots
  that should run; resuming only clears the halt.
- **Manual orders.** `ManualOrderRequest` is a market buy in quote or a market sell in base for a bot that should run
  and runs. It is submitted under an engine order ID or rejected with a code, and a request not taken within 60 seconds
  expires.
- **Domain events** are past-tense facts (`BotStarted`, `BotKilled`, `BotDegraded`, `OrderIntentRejected`, ...). The
  outbox (stage 5.3) delivers them to the event feed, alerts and notifications. Every transition and every refused
  transition has a Domain test.

## Consequences

- The runtime and its stores call these methods instead of encoding rules in SQL; integration tests verify only
  persistence, fencing and delivery.
- Client order ID formats belong to venue modules; the Domain treats the ID as opaque.

## Amendment: persistence and outbox (stage 5.3)

- **Tables.** `bots`, `bot_leases`, `bot_checkpoints` (versioned JSONB of `GraphEngineCheckpoint` with the applied
  parameter values), `bot_order_intents` (unique per bot, engine order and kind, ordered by a sequence) and the
  single-row `trading_halt`. A bot refers to its owner, strategy, strategy version and credential.
- **Row locks.** Bots, leases and intents change under a row lock taken when they are loaded (`GetForUpdate`) in the
  writing transaction, which serializes the owner's commands, the runtime and the dispatcher. This replaces version
  numbers for these aggregates (ADR 0004 amendment).
- **Fenced root.** `BotRootCommitter` locks the lease, checks holder, epoch and expiry through `BotLease.IsHeldBy`,
  then saves the checkpoint, the new intents (a repeated intent is ignored) and the bot in one transaction. A fenced
  writer gets `BotLeaseLostException` and nothing is written.
- **Outbox.** Every transaction stores the Domain events of the aggregates it wrote in `outbox_messages` before
  commit. The Worker's dispatcher delivers due messages in sequence order, one transaction each, to MediatR handlers
  that only write to the database and then marks the message delivered; a crash or a failed handler rolls both back,
  so each event takes effect once. Failed deliveries wait 1, 2, 4, ... seconds up to five minutes; a message of an
  unknown type is kept with `unknown_event_type`. Several dispatchers may run (`SKIP LOCKED`). Side effects outside
  the database, such as Telegram, get their own delivery queue fed by a handler.

## Amendment: bot API (stage 5.4)

- **Endpoints.** `POST /api/bots` creates a stopped bot; `GET /api/bots` and `GET /api/bots/{id}` read the caller's
  bots; `POST /api/bots/{id}/start` and `/stop` change the desired state; `PUT /api/bots/{id}/parameters`,
  `/risk-limits` and `/foreign-orders` change settings. Another user's bot, strategy or credential answers 404.
  Decimals are exact invariant strings.
- **Creation** takes the venue from the credential, resolves the series through the venue descriptor, checks the
  instrument's rules, requires a value for every parameter of the latest strategy version (whole numbers for integer
  parameters, `parameters_mismatch`), compiles that version with them and reads the venue's maker and taker rates with
  the credential through the venue port `IVenueFeeRateReader` (maker for limit, taker for market orders). A venue
  without that port answers `503 fee_rates_unavailable`. Creation runs outside a transaction because of that read.
- **Start** compiles the latest version with the bot's values when no run exists, refuses a bot whose run exists but
  whose checkpoint is missing (`missing_checkpoint`) and forgets a stopped run that holds and spent nothing (Profit
  Fox Designer ADR 0068, widened): no order working or waiting to be submitted or cancelled, no base held or reserved,
  no quote reserved or spent, no cost, profit, fee or last buy price, every intent final (`Sent` or `Rejected`) and no
  fill of the bot. Its orders may have ended, rejected or cancelled, without a fill, as an entry the owner confirmed
  absent does; the next run starts from `OnStart` and continues the order identifiers after the bot's last intent, so
  no client order identifier repeats at the venue. A halt answers `409 trading_halted`.
- **Settings.** Parameters and allocation change only on a stopped bot (`409 bot_must_be_stopped`); with a stored run
  the cap must cover cost basis plus reserved quote (`allocation_below_committed`) and the edited graph must compile
  (`409 parameters_incompatible`). Risk limits change at any time; the foreign-orders policy only while stopped.
- A credential that a bot uses cannot be deleted (`409 exchange_credential_in_use`).

## Amendment: live candles (stage 5.5)

- **Port.** `ILiveCandleSource` is a venue port: a subscription yields confirmed closed candles of one series in
  open-time order without gaps; a subscriber that names its last candle first receives the gap from public history.
- **Bybit.** `Venues.Bybit` reads the public Spot WebSocket topic `kline.{interval}.{symbol}`, never authenticated, with
  one feed per series in a process shared by its subscribers. A candle newer than the last is published once. A feed
  silent for its timeframe plus `Venues:Bybit:LiveFeedStallMargin` (60 s) reconnects, reports itself stale, fills the
  gap from REST history and reports itself healthy at the next candle. A REST fill with a gap is an error.
- **Stale feed.** The venue module sends `MarkFeedStaleCommand`, which sets or clears `StaleFeed` on the bots that should
  run on the series.

## Amendment: running bots in the Worker (stage 5.5)

- **Host.** The `live` role runs `BotRuntimeHost`. Every `Bots:Runtime:PollingInterval` (2 s) it beats the `live`
  heartbeat, stops the runners of bots that should no longer run and takes the leases of bots that should
  (`AcquireBotLeaseCommand`; 30 s leases renewed every 10 s). A failure of one bot does not hold up the others. The API's
  readiness reports Degraded when bots should run but the `live` heartbeat is older than three polling intervals.
- **No gateway.** `IOrderGateway` is a venue port that exists only where trading is enabled; it owns the venue's client
  order ID format and streams the bot's order updates. A leased bot whose venue has no gateway in the process degrades
  with `NoGateway`, its lease is released, and it waits for the owner. Nothing is sent.
- **Runner.** One runner per leased bot reports `Starting`, opens its engine, reports `Running` and processes live
  candles and venue updates through one sequential queue. A stop ends it after the current root and reports `Stopped`;
  a lost lease ends it without writing; any other failure reports a runtime failure (automatic restart with backoff).
  The lease is released at the end. Every report is fenced by the lease (`ReportBotRuntimeCommand`).
- **Engine session.** A first run compiles the strategy version with the bot's values, warms on public closed history
  and starts at its last candle, emitting `Start` once. A stored run restores the checkpoint with `RestoreWithoutResume`
  on the history ending at its last candle, warms on the candles missed since without `CandleClosed`, and drains one
  `Resume(Restart)`; it never starts again. A live candle is an open, a close and, once indicators are ready, a
  `CandleClosed` root. Each root is committed with a submit intent for every new pending order and a cancel intent for
  every order that turned pending-cancel. A root after a live candle resets the restart streak.
- **Released leases** expire at the moment of release, so another runtime may take the bot at once.

## Amendment: order intent delivery (stage 5.6)

- **Dispatcher.** The `live` role delivers due intents one at a time (`DispatchOrderIntentCommand`); each step is a short
  transaction and no transaction spans a venue call. A process considers only intents of bots on venues it has a gateway
  for, so an intent it cannot send never holds the others back. A process delivers submits only for the bots it leases, and
  cancels also for bots no runtime leases. An intent waits behind earlier undelivered intents of its bot, except that
  a cancel passes submits of other orders, and every submit of a bot that should stop.
- **Submit.** It is claimed only while the bot should run, is healthy, trading is not halted and the holder's lease has
  an open submit gate; the same check repeats, under row locks, when the gateway records the place start just before
  the request may leave the process. A blocked submit is released, and rejected with `KillSwitch` when a kill or the
  halt blocked it before it left. A submit that may have reached the venue is looked up before any retry.
- **Outcomes.** Acceptance and fills arrive through the gateway's updates. A definitive rejection is stored and handed
  to the bot's runner through an in-process bus; a rejection the runner never read is applied from storage when the
  engine opens. A failure retries with backoff; the tenth fails the intent, degrades the bot with `IntentFailed`, and
  the host stops its runner without overwriting that status. A bot that should stop holds its waiting submits
  (`HeldSubmit`).
- **Gate.** The runner opens the submit gate after its engine opened; venue reconciliation (stage 5.8) comes before it.

## Amendment: reconciliation before the submit gate (stage 5.8)

- **When.** After the engine opened and before the submit gate opens, the runner reconciles the bot with its venue
  (`BotReconciler`, `IOrderGateway.Reconcile`). The bot stays `Starting` until it succeeds. Updates from the private
  order stream follow in a later step; a stream that fails restarts the runner, and the bot reconciles again.
- **What is asked.** Every engine order that is not final, and every order whose submit or cancel failed, is being
  sent, or had a place start. An order may have reached the venue once the engine saw it accepted, its submit was sent,
  or a place request may have left the process.
- **Venue answer.** Updates for the orders it found, the orders with whether they are final, and a safety reason when
  the bot must not trade. Bybit (ADR 0017) refuses a read-only key, a key without Spot trading or with withdrawals
  (`KeyPermissions`); an order that may have reached Bybit but is not found (`UnresolvedOrder`); and an open order or
  recent execution on the symbol with another client ID prefix or another bot's (`ForeignOrder`), unless the bot allows
  foreign orders. A venue that cannot answer throws.
- **Applying it.** The updates go to the engine first. Then, fenced by the lease (`ApplyBotReconciliationCommand`), a
  submit the venue found is resolved, a cancel of a final order is resolved, an unsent failed submit is tried again,
  `ExchangeUnavailable` is cleared and a safety reason degrades the bot. A failed cancel of an order that is still
  active is `UnresolvedOrder`. A degraded bot's runner ends without overwriting its status.
- **Unavailable venue.** The bot is marked `ExchangeUnavailable` and asked again after 5, 15, 30 and then every 60 s.
  A stop or a lost lease ends the waiting at once.
- **Safety exceptions.** A venue reports a state the bot cannot trade through with `OrderGatewaySafetyException`, such
  as Bybit's fee in an unsupported asset (`UnsupportedFeeAsset`). During delivery it degrades the bot and returns the
  intent for a later lookup; from the update stream it degrades the bot and ends its runner.
- **Deferred.** Resolving an order the owner confirms absent and accounting fills found only in execution history are
  left for later steps; until then such a bot stays degraded for the owner.

## Amendment: manual orders (stage 5.11)

- **Request.** The owner submits a market order in a bot that should run and runs (`POST /api/bots/{id}/manual-orders`):
  a buy sized in quote, a sell in base (Profit Fox Designer ADR 0091). It is stored in `bot_manual_orders`; a bot has
  at most one pending request (409 `manual_order_pending`), and a stopped bot takes none (409 `bot_not_running`).
- **Root.** The runner that holds the lease looks for the pending request every second and processes it as a root on
  the bot's queue: the engine submits it against the last closed candle with the reserved origin `$manual` and tag
  `manual`, and the root's commit resolves the request (`Submitted` with the engine order, or `Rejected` with the
  engine's reason) in the same transaction as the checkpoint and the new intent. From there the order follows the
  normal path: per-order limit, gateway, reconciliation.
- **Expiry.** A request nobody took within 60 s reads as rejected with `expired`; the runner rejects it so without the
  engine, and a new request stores it so. A request that expires while its root runs fails that commit, so the engine
  restarts from its stored state without the order.

## Amendment: seller fees (ADR 0022)

- A venue whose marketplace charges the seller per unit returns a seller fee schedule from `IVenueFeeRateReader`
  instead of maker and taker rates. The bot stores it (`bots.seller_fees`) with both rates zero, its engine account uses
  it, and `GET /api/bots/{id}` returns it as `sellerFees`. A change of the schedule is a fee rate change.
- `IVenueExecutionDefaults` gives a venue's defaults for a new backtest; `GET /api/market-data/venues` returns them as
  `executionDefaults`. A run stores what its request gave, never the defaults.

## Amendment: venue account protection (ADR 0022, C7)

- `VenueAccountProtection` is a safety reason: a venue module whose account protection opens sends
  `StopBotsForVenueAccountCommand`, which degrades every bot of the credential that should run. Orders stay as they are.
- Resuming the protection (`POST /api/exchange-credentials/{id}/protection/resume`, or the module by itself for transient
  noise) sends `RearmBotsForVenueAccountCommand`: those bots return to Created, and the runtime leases and reconciles them
  before any submit.

## Amendment: lock order

- Every transaction that locks more than one of a bot's rows locks them in one order: the lease, then the bot, then its
  order intents. A writer that starts from an intent reads it unlocked to find its bot, locks the lease and the bot, and
  only then locks the intent and checks it again. Opposite orders in the dispatcher's claim and failure paths and in
  lease acquisition let two writers wait on each other, which Postgres broke as a deadlock and the runtime reported as a
  runtime failure.

## Amendment: live market closures (ADR 0023, TC3)

- The live candle port is unchanged. When a live candle follows a gap, the engine session reads the venue's history of
  the gap (`IHistoricalCandleSource`): its candles are processed in order and its market closures are recorded in the
  engine, so position age leaves them out; a gap that neither covers still fails the runner. A venue that fills its own
  gaps, like Bybit, rarely reaches this path; when it does, the missed candles come from its history instead of a restart.
- Warm-up, restore and graph change read closed history that reaches back over closures until it has the needed candles
  (at most 16 times, each doubling the span); a venue without closures is read once, exactly as before. The closures of
  that history go to the restored or new engine.
- Runtime reads (warm-up, rules, gaps) use the bot's own credential as market data access only for a venue that
  declares `IVenueMarketDataAccess`; for any other venue no credential is decrypted for reads.

## Amendment: waiting orders (ADR 0023, TC4)

- A venue module that holds a bot's working order locally (`LocalAccepted`) reports why with `RecordOrderWaitingCommand`:
  a neutral `OrderWaitingCode` (`PriceBand`, `SessionClosed`, `CarryOver`, `SettlementHold`, `SingleBuyOrderSlot`,
  `AwaitingConfirmation`, `RateLimited`), an optional end time and optional price bounds; a null code ends the wait. The
  engine still sees only a working order.
- `bot_order_waiting` keeps one row per waiting order, changed in one transaction per report from any caller (a runner
  turn or a module's own worker). A wait's start is reported in the feed (`OrderWaiting`) and its end once
  (`OrderWaitingEnded`); a new reason for the same order ends the current wait and then starts a new one, while new
  details of the same reason and repeated reports are not reported. A root whose checkpoint no longer has the order
  working (filled, cancelled or rejected) ends its wait, whatever the venue reported.
- A module that holds an order locally asks `AuthorizeWaitingReleaseCommand` right before it sends that order later
  outside a runner turn. It answers with the gates of a place start: the bot should run and is healthy, no trading halt,
  and a live lease with an open submit gate. The lease and the bot are read under their locks in one transaction.
- `GET /api/bots/{id}/waiting-orders` lists them for the owner; the bot page shows them with fixed texts such as
  «Ждёт ценового коридора (коридор 90–110)» or «Перенос на следующую торговую сессию».
