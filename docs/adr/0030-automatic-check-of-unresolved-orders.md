# ADR 0030: Automatic Check of Unresolved Orders

Status: Proposed, 2026-10-27. Built in two pull requests: T-Invest, offline carry-over, transparency and the recheck first;
Bybit and Steam second (sections 5 and 6). Proven on mocks only; the owner reviews it in the pull requests.

## Context

A bot stops in `Degraded/UnresolvedOrder` when the venue gives no single answer about an order that may exist. The only way
out was the owner's confirmation ("the order is absent", ADR 0016; for an order T-Invest accepted and removed, ADR 0028),
after the owner compared the broker's app by hand. The real case is T-Invest DAY orders that the exchange removed at the end of
a day while the bot's runner was down. ADR 0028 found why carry-over (ADR 0023, T12b) did not run: it needs the bot's order
stream in this process, and a bot that failed has none. The owner asks that the bot checks and closes such cases itself, for
every venue, and safely.

The rule is the one for money: when anything is uncertain the bot stops and the owner decides. Automation sends no new order
and cancels no real order beyond the carry-over that already exists; the owner keeps the desired state of every bot.

## Decision

### 1. Prevention: carry-over without a runner

The carry-over safety never rested on the runner. It rests on the attempt being marked (`cancelled_by = CarryOver`) before the
cancel leaves and on the final state being read back and recorded. Only the delivery of that state to the engine used the stream.
Keeping the runner alive to the end of the session cannot be promised (a crash, a deploy, five spent restarts), so the module
no longer needs it:

- `TInvestWaitingOrdersWorker` carries over the day orders of a bot whose stream is not open in this process when the core
  authorizes it (`AuthorizeOfflineCarryOverCommand`): the owner's desired state is `Running`, the bot is healthy or degraded
  only by `RuntimeFailure` (a failed run that restarts), no trading halt is on, and no runtime holds an unexpired lease. A bot
  the owner stopped, a killed bot and a bot stopped by a safety reason keep their orders: stopping leaves open orders as they
  are, and ADR 0028's objection to cancelling what the owner may have left on purpose stands.
- The carry-over itself is unchanged: mark, cancel by request ID, read the final state, record the attempt's executions and, for a
  cancelled attempt with whole lots left, the next attempt that waits with `CarryOver`. Sending that remainder still needs the
  runner and the submit gate (`MayRelease`); nothing is sent for a bot nobody runs.
- The engine hears it at the next start. Reconciliation now replays what the table holds: a waiting later attempt is preceded by the
  cumulative `Accept` and `PartialFill` of its earlier attempts, and a final attempt that executed all its lots ends the engine
  order with the recorded totals when T-Invest no longer answers for it (it answered `NotFound` for a past day's order, ADR 0028).
  The engine ignores a total it already booked, so the replay is harmless when the runner had heard it.

### 2. Closing an order the exchange removed, by evidence (T-Invest)

For an attempt T-Invest accepted (`Active`, with an exchange order ID) whose request ID it no longer knows, reconciliation closes
the engine order without the owner when all of these hold, checked in this order; the first that fails stops the proof, and a read
that fails is a failed check, never a pass:

1. `NotFoundByRequestId`, `NotAmongActiveOrders`: no active order of the account on the share carries the request ID or the
   exchange order ID.
2. `NoExecutionRecorded`: neither the engine (`ExecutedBase` of its progress) nor any attempt of the order booked an execution.
3. `NotFoundByExchangeId`: `GetOrderState` by the exchange order ID answers `NotFound`.
4. `TradingDayEnded`: the trading day that holds the attempt's last recorded instant ended at least `AbsenceSettleDelay` (1 hour)
   ago, from the schedule read for that range (`TradingSchedules`); a day outside the vocabulary of ADR 0023, a gap between days
   and an unreadable schedule are unknown, not ended. An order older than `AbsenceMaxWindow` (10 days) is left to the owner.
5. `NoExecutionInOperations`: `OperationsService/GetOperationsByCursor` for the share, from five minutes before the attempt was
   planned to now, lists nothing except orders cancelled with nothing executed. Any other state, even one the module does not
   know, counts as an execution. `GetOperations` is marked deprecated by the broker and cannot be paged, so it is not used.
   Pages are 1 000 items and at most 5 are read; more than that, or a repeated cursor, is incomplete and proves nothing. Calls go
   through the token's `Operations` request limit (`TInvestRequestLimiter`).
6. `PositionMatchesEngine`: `GetPositions` reports the share with nothing blocked and a total equal to the engine's expected
   position (`BaseTotal` of its checkpoint, sent as `ExpectedBase`); an answer while limits are still loading, or an engine
   without a checkpoint, proves nothing.

When all hold, the attempt becomes `Final` with `cancelled_by = Owner` (nothing executed) and the gateway reports `ConfirmCancel`,
as ADR 0028 does after the owner's confirmation; strategies hear `Cancelled`, the reservation is released and the bot goes on.
The owner's confirmation stays, unchanged, as the fallback; when it was given, ADR 0028's three checks decide and these do not.

A check that fails leaves the order unresolved as before, and the stop carries the check: `Degraded` with detail
`AutoCheck:<failed check>`, which the bot page words (for example "позиция по бумаге не равна ожидаемой ботом"). Any execution
of the share among the account's operations in the window blocks the proof, even one of another order; that is the cost of not
guessing which operation belongs to which order, and it falls back to the owner. ADR 0033 relaxes this for the bot's own, already booked executions.

Not covered: an attempt whose place request had no clear answer (`Unknown`), a waiting or planned one and a partly filled one.
They are not "accepted and removed", and the owner decides.

### 3. A bot already stopped is checked again

A bot that should run and stopped on `UnresolvedOrder` would stay so until the owner acts, even when the proof would pass an hour
later. `UnresolvedOrderRecheckHost` (Worker, live role) lists such bots every 5 minutes and, per bot, `RecheckUnresolvedOrdersCommand`
asks the venue's gateway `CanProveAbsence` (a new default method of `IOrderGateway`, false unless a gateway implements it): reads
only, no stream, no attempt moved. When every order the venue cannot confirm would be closed and nothing else would stop the bot,
`Bot.ClearUnresolvedOrder` returns it to `Created`, the runtime takes it, and its own reconciliation closes the orders and
records the proof. A bot the owner stopped is never touched (`DesiredState` is `Running` in the condition), and a bot stopped for any
other reason is left alone.

### 4. Transparency

Each automatic closure is recorded where the owner looks:

- `OrderIntent.ConfirmExpiredAutomatically` marks the intent (`confirmed_absent_automatically`, with a time and no user; migration
  `202610270001`) and raises `OrderIntentAbsenceProven`, whose checks are stable codes without account data.
- The bot's feed (`bot_events`) gets `OrderAbsenceProven` with `engineOrderId` and `checks`; the bot page words it as "заявка N
  закрыта автоматически, потому что …" with one phrase per check. Returning a stopped bot to the runtime shows as `Recovered`.
- The unresolved-orders card says that the bot checks such orders itself and that the confirmation below is the fallback.

### 5. Other venues

| Venue | Unresolved state | Can it be proven? | Decision |
|---|---|---|---|
| Bybit | A submit whose place request may have left (engine `PendingSubmit`; intent `Pending`, `Sending` or `Failed` with a place start) that neither `order/realtime` nor `order/history` knows by `orderLinkId` | Only weakly. ADR 0017 lists "whether any response proves that an ambiguous submit was not accepted" and how long history lags as owner checks still open. A long wait, an execution list with nothing of the order and a base balance equal to the engine's together make a lost order very unlikely | Automatic, see section 6 |
| Bybit | An order the engine saw accepted (`Active`, `PendingCancel`) that Bybit no longer knows | No: Bybit keeps accepted orders in history, so this is a contradiction in its data, not an expiry | Owner |
| Steam | A buy with no Steam order ID that was being placed or whose outcome is unknown (`Placing`, `Unknown`) | Yes: the owner's own check, automated: no buy order of the account on the item beyond the journal's, no unattributed purchase since the place request in freshly read history | Automatic after a settle delay, see section 6 |
| Steam | A buy awaiting its mobile confirmation (`Approving`), or a buy Steam gave an ID | No: the owner may still approve it in the app, or it may execute | Owner |
| Steam | A sell in `Listing`, `Approving`, `Delisting` or `Unknown` | No: a listing and an item that left or did not return cannot be told apart from Steam's pages with the module's readers | Owner |

### 6. Bybit and Steam

Both reuse the shared parts: `ReconciledOrderInternal.AbsenceProof`, the mark on the intent, the feed entry, `CanProveAbsence` and
the five-minute recheck. The engine order ends as a rejection with the new reason `AbsenceProven` (update flagged
`OwnerConfirmedAbsent`, as the owner's confirmation does for a submit); its intent is resolved and marked in the same pass.
`ReconcileOrderInternal` also carries `AcceptedByEngine` and `PlaceStartedAt`.

- **Bybit** (`BybitAbsenceProofs`). Only a submit the engine never saw accepted, with a known place start. All must hold, and the
  first that fails stops the proof (the stop carries `AutoCheck:<code>`; a failed read is a failed check): the place request
  is at least 24 hours old and at most 6 days (the execution list covers 7); the symbol's executions since the request
  (`/v5/execution/list`, paged) show none with the order's `orderLinkId`; and the base coin's wallet balance on the UNIFIED
  account (`/v5/account/wallet-balance`) equals the engine's expected position. Coins the owner holds outside the bot make the
  balance differ, and the order stays with the owner. Both reads were already on the read client's allowlist.
- **Steam** (`SteamOrderGateway`). Only a buy in `Placing` or `Unknown` without a Steam order ID, a day after its place request.
  The evidence is the owner path's: no buy order of the account on the item beyond the journal's known ones and no unattributed
  purchase of the item since the place request (60 seconds of skew), read fresh, with the history read required to be known. The
  day replaces the owner's eyes on a history that lags. The inventory is not compared: items on listings leave it, so it cannot
  say how many the bot should hold.

The delays are constants (24 hours), not settings: they are unverified policy, and a change should be a decision.

Left to the owner in both: anything the engine saw accepted at Bybit, a Steam buy awaiting its confirmation or already given an ID,
and every Steam sell.

## What is not verified

Everything above is proven against mocks. Not checked on the real broker (T-Invest): that `GetOperationsByCursor` lists a DAY order the
exchange removed (state and `quantity_done`), the operations' delay after a session, that `TradingSchedules` answers for dates in
the past (if it refuses them the proof never passes and the owner decides), that `GetPositions` counts shares and not lots
(the engine's position is in shares), and how a share owned outside the bot shows: a position held outside the bot makes
`PositionDiffers`, so such an account stays with the owner. The owner's checks of ADR 0023 apply. Not checked on the real venues for Bybit and Steam: that 24 hours exceeds Bybit's history lag and Steam's market history delay (the open owner checks of ADR 0017 and 0022), and that Steam's history lists every purchase the journal does not attribute.

## Consequences

- A DAY order of a failed bot is carried over and no longer ends as `UnresolvedOrder`; an order that did expire is closed by the bot
  when the broker's records agree, and left to the owner when they do not.
- Safety rests on conjunction: a missing order, a finished day, no execution in operations and an equal position. Nothing here
  sends or cancels an order beyond carry-over, and no automatic path starts a bot the owner stopped.
- A real fill that the operations show blocks the proof for every order of the share in the window; the owner reads the account.
- Tests: Domain (`ConfirmExpiredAutomatically`, `ClearUnresolvedOrder`), Application (`AutomaticAbsenceTests`), the gateway and
  the worker on the mock Invest API (`TInvestAbsenceProofTests`, `TInvestCarryOverTests`), persistence, and the frontend.
