# ADR 0024: Deferred Orders

Status: Proposed, 2026-10-26. No code until the owner accepts it; the owner's answers to the questions at the end amend it.

## Context

A live bot's strategy emits an order the moment its rule fires, but a venue cannot always take that order now. Steam
locks a bought item for a trade hold; T-Invest has price bands, closed sessions and carry-over; Steam allows one buy
order per item and pauses an account's requests. ADR 0022 and ADR 0023 (TC4) solved it inside the venue module: the
gateway accepts the order with `LocalAccepted`, keeps it in its own journal, and reports a neutral `OrderWaitingCode`
(`bot_order_waiting`) that the feed and the bot page show. The module sends the order later, after asking
`AuthorizeWaitingReleaseCommand`.

Found on the dev bot on 2026-10-26: `ladder` filled its entry and at once emitted `ladder.exit`, a limit sell of one
item at 692.00. The item was still under its trade hold, which Steam's gateway would have shown as `SettlementHold`.
But the dispatcher never reaches the gateway: before the first place of a submit it values the order (ADR 0018: a sell
at its quantity times its limit price, 692) and rejects it with `RiskLimit` against the bot's `MaxOrderQuote` of 600.
The result is a position and no exit, and the owner sees neither a waiting sell nor a reason.

Three things are wrong in the current shape:

1. **The risk check runs at the wrong moment for the wrong thing.** It measures a sell against a limit meant for
   *new quote exposure*. A sell of held base commits no quote and cannot exceed what the bot already holds, which the
   buy side admitted under the same limit and the allocation cap.
2. **"Waiting" exists only after the gateway accepted.** An order the core refuses or delays before the gateway is
   invisible. Every venue also re-implements the same lifecycle (accept locally, wait, ask to release, send).
3. **Deferral is not a core concept.** The core cannot say "this intent is not due yet because of X, check again at T",
   so it cannot show it, cancel it uniformly, or reason about it in a kill, stop or restart.

## Decision

### 1. The core owns the deferral; the venue answers a question

A **deferred order** is a submit that was accepted by the bot (the engine holds it as a working order, as it does
today for `LocalAccepted`) but is not yet delivered, because a *condition* does not hold. The condition and its reason are
core data; evaluating it needs venue knowledge, so the venue answers a pure, read-only question.

- **Intent state.** `OrderIntentStatus` gets `Deferred` between `Pending` and `Sending`. A deferred intent carries a
  `Deferral`: a neutral `OrderWaitingCode` (the existing enum, unchanged: `SettlementHold`, `PriceBand`,
  `SessionClosed`, `CarryOver`, `SingleBuyOrderSlot`, `AwaitingConfirmation`, `RateLimited`), an optional `Until`, optional
  price bounds, `Since`, and `NextCheckAt`. `OrderIntent.Defer(...)` and `OrderIntent.Resume()` are the only transitions;
  both have Domain tests (architecture rule: every aggregate state transition has one). `Defer` is legal from `Pending`
  or `Sending` before any place start (`PlaceStartedAt` is null); never after one, because after a place start the
  order may be at the venue and only a lookup applies (ADR 0016).
- **Venue port.** `IOrderReadiness` (Application port, venue implements it, like `IOrderGateway`):
  `Evaluate(botId, OrderSubmitInternal, ct) -> OrderReadinessInternal` with `Ready`, or `Deferred(code, until?, low?, high?)`.
  It must not send, reserve or write to the venue; it may read the module's own journal and cache (Steam: inventory
  unlock times it already keeps). A throw or an unreadable answer means *not ready*, with `RateLimited`-like retry,
  never *ready*.
- **Who checks and when.** The dispatcher, which already owns delivery. A due `Pending` intent is evaluated in the claim
  step before it becomes `Sending`; a `Deferred` intent is a delivery candidate again at `NextCheckAt`. `NextCheckAt` is
  `Until` when known (clamped to the dispatcher's polling floor of 5 s and a ceiling of 15 min), otherwise the venue's
  advertised recheck interval. Steam's own background advance (listing unlocked assets) is replaced by this same
  re-evaluation in its first stage-two step, not run in parallel; see Stages.
- **Releasing is the existing path.** `Ready` moves the intent through the unchanged `Claim -> MarkPlaceStarted ->
  Submit` path. Every gate of a place start (bot should run and is healthy, no halt, live lease with open submit gate)
  still applies at that moment; `AuthorizeWaitingReleaseCommand` becomes a thin wrapper of the same check and is kept
  for modules that still hold orders themselves.
- **Engine view.** Unchanged. The dispatcher publishes the same `LocalAccepted` update the gateway publishes today when an
  intent is deferred, so strategies see a working order, and a later fill, cancel or rejection flows as before. No
  engine type changes, so Bybit's fingerprints and pinned replay hashes stay byte-identical.

### 2. Risk limits: when and on what

The per-order limit stays a pre-send, pre-first-place check, so a lookup of a submit that may have been sent is still
never blocked. What changes is *what it measures* and *when for a deferred order*:

- **Exit orders are not new exposure.** A sell (limit or market) whose quantity does not exceed the base the bot holds
  and has not already reserved for another sell is **reduce-only**. A reduce-only sell is exempt from `MaxOrderQuote`.
  The cap exists to bound how much quote one order commits; a reduce-only sell commits none, and what it can sell is
  already bounded by buys admitted under the cap and by the allocation (ADR 0018, "Allocation").
  A sell larger than holdings cannot exist in the engine (it rejects first), so the exemption has no loophole through
  the dispatcher; the dispatcher still re-checks it against the checkpoint rather than trusting the intent.
- **Safety kept by a price floor instead.** The real danger of a sell is *giving the item away*, which a high notional
  never was. A reduce-only limit sell is rejected with `RiskLimit` when its limit price is below
  `SellPriceFloorRatio` times the last closed candle's close (default `0.5`, a bot risk limit changed only by the
  owner like the others); a market sell is not floored. Deferred or not, the floor is checked at creation, so a typo is
  rejected at once, not a week later, and again at release against the then-current close.
- **Buys unchanged.** Buy intents are measured and rejected exactly as today, at the first place; a deferred buy
  (single-slot, rate limited) is measured when it is released as well as when it is created, so the limit is never
  checked against a stale decision.
- **When it applies, summarized.** Creation (dispatcher claim, first evaluation): both checks, so a hopeless order fails
  immediately and visibly. Release (just before the place start): the buy limit and the sell floor again against current
  data. A deferral never skips a check and never defers a rejection: an order that is rejected is rejected now.
- **Kill, halt, stop.** All three are "not allowed to send" states and behave exactly as for a pending submit: a kill
  or halt rejects a `Deferred` submit with `KillSwitch` in the kill's transaction (it never left the process); a bot
  that should stop keeps its deferred submits held (`HeldSubmit`, as waiting submits do) and shows them; a restart or
  lease takeover re-reads them from `bot_order_intents` and evaluates them again. Holdings are never sold by a kill
  (ADR 0018); a deferred exit that a kill rejected is simply gone, as a pending one is today, and the position is the
  owner's to close.

### 3. Lifecycle

| Event | Intent | Engine | `bot_order_waiting` / feed |
|---|---|---|---|
| Strategy emits the order | `Pending` | `PendingSubmit` | none |
| Evaluation says wait | `Deferred` (code, until) | `LocalAccepted` | wait started (`OrderWaiting`) |
| Rechecked, still waits | `Deferred` (new `NextCheckAt`, details) | unchanged | details changed, not re-reported |
| Rechecked, reason changed | `Deferred` (new code) | unchanged | old wait ended, new started |
| Ready | `Sending`, then `Sent` | via gateway | wait ended (`OrderWaitingEnded`) |
| Strategy cancels while deferred | `Deferred` -> `Rejected` (`Cancelled by owner`)... see below | `ConfirmCancel` | wait ended |
| Kill / halt | `Rejected` (`KillSwitch`) | rejected | wait ended |
| Owner stops the bot | `Deferred`, held | working | stays; reason adds "bot stopped" |
| Restart / takeover | `Deferred`, re-evaluated | restored from checkpoint | row re-created if missing |
| Evaluation says it can never be ready | `Rejected` with the venue's reason | rejected | ended |

A cancel of a deferred order is local: the dispatcher finishes it as it finishes a cancel of a pending submit, without
a venue call, and publishes `ConfirmCancel`. This is today's gateway behavior ("a cancel of an order still waiting
locally is local", ADR 0022) moved to the core for deferrals the core owns. `bot_order_waiting` stays the projection of
the current deferral; the intent is the source of truth and the projection is written in the same transaction (ADR 0004).

### 4. Backtest and replay

Deferred orders are a **live-only concept**. The engine and replay are unchanged and must not depend on live code or
venue modules (AGENTS.md). Venue conditions that matter for results already have deterministic replay models: ADR 0022
C5 (`SettlementHold` as an engine setting with lots unlocking at explicit market time and partial sell fills), ADR 0023
TC1/TC2 (closures and candles without trades). The deferral never feeds back into replay, no `TimeProvider` is read by
the engine, and replay hashes are unaffected. A test must pin that a Steam backtest with the hold produces the same
ledger before and after the change.

### 5. Contracts and API

- **Domain.** `OrderIntentStatus.Deferred`, `OrderDeferral` value object, `OrderIntent.Defer/Resume`, event
  `OrderIntentDeferred` (feeds the existing `OrderWaiting` feed items).
- **Application.** `IOrderReadiness` and `OrderReadinessInternal` in `Bots/Ports` and `Bots/Contracts`; `OrderReadiness
  InternalConverter`; a change to `DispatchOrderIntentCommandHandler`. Migration `{version}_add_order_intent_deferral`
  adds nullable `deferral_*` columns and the new status value to `bot_order_intents`.
- **API.** `GET /api/bots/{id}/waiting-orders` keeps its shape (`code`, `until`, `low`, `high`, `since`) and gains
  `source: "intent" | "venue"` and, when the order is deferred before delivery, `nextCheckAt`. Decimals stay exact strings.
  `GET /api/bots/{id}/open-orders` (the "Стоит на бирже" block) returns deferred sells with status `Deferred`.
  OpenAPI client is regenerated.

### 6. UI

The active-orders block of the bot page gets a status `Deferred` next to `Active` ("Стоит на бирже"): label
«Отложен: ждёт снятия трейд-холда до 01.11.2026 00:00 UTC» (the existing `waitingText`), with the order's side, quantity
and price, the reason, `Until` and the time of the next check. The feed shows `OrderWaiting` / `OrderWaitingEnded` as
today. A rejected exit shows its `RiskLimit` or price-floor rejection with the numbers (price, floor, close), so the owner
sees why. The owner can cancel a deferred order from the block; nothing else is added (YAGNI).

## Stages

Each stage is its own pull request with Bybit's fingerprints and pinned replay hashes unchanged.

1. **Core and contracts.** `Deferred` status, `Deferral`, `Defer/Resume` with Domain tests, migration, `IOrderReadiness`,
   dispatcher changes (evaluate at claim, recheck at `NextCheckAt`, local cancel, kill/halt/stop handling), reduce-only
   exemption and the price floor with its bot setting, `waiting-orders` and `open-orders` contract changes. Bybit has
   no readiness implementation and is always `Ready`; mocked-gateway tests cover every row of the lifecycle table.
2. **Steam as the first consumer.** `SteamOrderReadiness` over the asset reservations/unlock times already in
   `SteamSells`; a sell with all or some assets locked is `Deferred(SettlementHold, earliest unlock)`; buys: single slot,
   rate limit. Move the "list assets that unlocked" step from the order worker into re-evaluation, leaving confirmations
   and finalization in the worker (they follow a send, not a deferral). Test against mock Steam (`SteamLadderTests`):
   exit of 692 against a limit of 600 is deferred, becomes `Sending` at unlock, is listed, a restart in between changes
   nothing, a kill rejects it, a stop holds it.
3. **UI.** The status, the texts, the cancel action, the rejection reason numbers; frontend tests.
4. **Other venues.** T-Invest (`PriceBand`, `SessionClosed`, `CarryOver`) re-expressed as readiness; Bybit stays `Ready`.
   Each venue is its own pull request after its stage 1 comparison with the venue-held behavior.

## Consequences

- One lifecycle for every venue; the bot page shows the same data whichever module holds the order.
- The exit is no longer lost to a quote limit meant for entries, while a mis-priced exit is still caught by the floor.
- More work moves into the dispatcher's claim step, and every evaluation is a venue-module read on a hot path; the
  module must answer from its own state and cache, never from a new network call per evaluation.
- Two holders of one deferral (core and a module that still holds locally) must not coexist for the same order type: the
  migration of each consumer replaces its module-side hold, in one pull request, to avoid double sends.

## Risks

- **Double send or lost send** if both the module and the dispatcher release the same order. Mitigation: a consumer
  migrates wholesale; the journal's unique `client_order_id` and the place-start rule remain the single guard.
- **Weakened limit.** The exemption changes ADR 0018 deliberately, for sells only. Mitigated by the reduce-only check
  against the checkpoint and by the price floor; recorded here and in the pull request.
- **Stale readiness.** A cached unlock time could release early; the venue still rejects an early listing, and release
  re-checks gates and floors. A cache older than the ceiling interval counts as not ready.
- **Long deferrals** (a Steam hold is up to 7 days) cross restarts, strategy changes and graph changes; the intent
  survives in the table, but a graph change that removes the order must cancel it locally. Covered by a stage-1 test.

## Questions for the owner

1. **Risk semantics.** Do you accept that a reduce-only sell is exempt from `MaxOrderQuote`, with a price floor instead
   (default 0.5 of the last close)? Alternatives: (a) value a sell at `min(limit, last close)`, which still blocks the
   exit whenever the close itself is above 600; (b) a separate `MaxExitQuote` setting; (c) keep as is and raise
   `MaxOrderQuote` by hand. I recommend the exemption plus the floor.
2. **Floor value.** Is 0.5 of the last close right for Steam, where the item price can be volatile, or should the floor
   be a per-bot setting you pick (the first release would read 0.5 until you change it)?
3. **Kill of a deferred exit.** After a kill the exit order is gone and the position stays, as for any pending submit.
   Do you want the exit to survive a kill (it only reduces exposure, like a cancel), or stay as designed?
4. **Cancel by the strategy.** When the strategy cancels a deferred order (a new signal), is a local cancel enough?
5. **Existing waiting orders.** Should Steam's sells already waiting at the venue module keep their module-side holds until
   the Steam consumer lands in stage 2, or is a one-time conversion needed?
6. **Order of stages.** The urgent bug is the lost exit. Do you want a smaller first pull request that only applies
   the reduce-only exemption and floor (no deferred status), so exits work on Steam now, and the full core after it?
