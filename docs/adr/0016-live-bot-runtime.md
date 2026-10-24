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
  Reconciliation resolves an undelivered intent once the venue state is known.
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
  whose checkpoint is missing (`missing_checkpoint`) and forgets a stopped run that never created an order. A halt
  answers `409 trading_halted`.
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
