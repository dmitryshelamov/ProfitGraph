# ADR 0048: The Owner Cancels an Active Order of a Running Bot

Status: Proposed, 2026-10-28. Section 4 is superseded by ADR 0051: the owner's cancel resumes the strategy. Supplements ADR 0016,
ADR 0024, ADR 0027 and ADR 0039; builds the "cancel of a manual order" that ADR 0027 left as a next step, for any of the bot's orders.
Proven on the scripted venue, unit tests and the frontend's fake API only; nothing was sent to a real venue. The owner reviews it in the
pull request.

## Context

The bot page lists the bot's open orders as its engine holds them (`GET /api/bots/{id}/open-orders`): the Ladder's exit and averaging
order, an entry being sent, an owner's resting manual limit order. Until now the owner could cancel one only at the venue (the broker's
app, Bybit's site, Steam), which the bot then learns about as a cancellation nobody asked for. ADR 0039 made such a cancellation resume the
strategy, so a Ladder places its exit again within a moment: right for "the exchange removed my order", wrong for "I want this order gone".
A manual limit order that never fills also blocks the next manual order (`TagActive`) until the owner cancels it at the venue (ADR 0027).

The bot's runtime already has everything a cancel needs, as long as the cancel goes through the engine: the engine marks an order
`PendingCancel` and keeps its reservation, the root's commit adds a cancel intent to the outbox in the same transaction as the checkpoint
(ADR 0016), the dispatcher sends it with its retries and failure rules, cancels a deferred submit locally (`CancelDeferredSubmit`, ADR
0024) and the venue's confirmation releases the reservation. Calling the venue gateway from the API would bypass all of it: the engine
would learn of the cancel as an unrequested one, the reservation and the Ladder's guards would not know, and a cancel and a strategy root
could race.

## Decision

### 1. A stored request, turned into the engine's own cancel by the runner

The owner's cancel is an explicit action on one engine order, handled exactly like the manual order (ADR 0027):

- `POST /api/bots/{id}/open-orders/{engineOrderId}/cancel` stores an `OrderCancelRequest` (`bot_order_cancels`, migration
  `202610280030`) and answers `202 Accepted` with the request; `GET /api/bots/{id}/order-cancels/{cancelId}` reads it back. A request is
  `Pending`, then `Requested` or `Rejected` with a stable reason. A request nobody takes within 60 s is `expired`, so a bot that stopped
  meanwhile never cancels it later.
- The runner that holds the bot's lease polls for the pending request (the manual order's interval) and takes it as a root on the bot's
  sequential queue. The engine's `CausalDispatcher.RequestOrderCancel` asks the account to cancel the order, which moves it to
  `PendingCancel` as when a strategy's cancel block cancels it. The root's commit saves the checkpoint, the new cancel intent and the
  request's resolution in one fenced transaction; a request that is no longer pending fails the commit, so nothing changes.
- From there it is the ordinary cancel: the intent is claimed and sent through the venue's gateway with the usual retries, backoff and
  failure handling of every cancel intent; the venue's confirmation (`CancelConfirmed`) ends the order, releases its reservation and the
  open orders, the chart's levels and the waits follow.
- The bot's feed records `OrderCancelRequested` with the engine order when the engine took the request.

At most one request per bot is pending (a unique index; a second POST while one waits is `409 order_cancel_request_pending`), which also
makes a double click or a retried POST harmless. The engine itself refuses a second cancel of an order already `PendingCancel`.

### 2. The stopped bot: refused

A cancel is accepted only while the bot should run and runs (`Bot.EnsureAcceptsOrderCancels`): otherwise `409 bot_not_running`, the code
of the manual order. A cancel through a kill-style intent (the kill switch cancels a stopped bot's orders without the engine, ADR 0018) was
rejected for one order:

- A stopped bot has no runner and no engine in memory, so its engine would learn of the cancel only at the next start's reconciliation, as
  a cancellation the venue made. ADR 0035 then resumes the strategy and the Ladder places the order again at once: the owner's cancel of a
  stopped bot would be undone by the very start that finds it.
- The kill switch is a whole-bot emergency stop with its own fencing; a second path that changes one order's state outside the runner's
  queue is what this ADR avoids.
- The owner of a stopped bot can cancel at the venue, with the same result at the next start, or start the bot and cancel it here.

Starting, reconciling, degraded or stopping bots are "not running" too; the page offers the button only while the bot runs.

### 3. Which orders, and what happens to each

| Order | Answer |
|---|---|
| Another user's bot | `404 bot_not_found`, as for every bot endpoint. |
| Not working in the bot's last checkpoint (filled, cancelled, rejected, never placed) | `409 order_not_open`. The runner checks again against the engine: an order that ended between the POST and the root rejects the request with `order_not_open`. |
| Already `PendingCancel` (the strategy or an earlier owner cancel) | `409 order_cancel_pending`; the runner rejects with the same code when it became so in between. |
| Active at the venue | Cancelled at the venue. |
| Partly filled | Cancelled like any active order. The gateways report the executions of a cancelled order before its cancellation (for example Bybit's `PartiallyFilledCanceled`, T-Invest's cumulative state of ADR 0039), so the filled part stays in the position; the reservation of the rest is released. The engine emits `Filled` only for a complete order, so the strategy is not told of the partial fill (as with any cancelled partial order). |
| Deferred submit that never left the process (ADR 0024) | Cancelled locally: the dispatcher rejects the submit as `CancelledBeforeSend` and tells the engine the order is cancelled; nothing reaches the venue. |
| Still being sent (`PendingSubmit`) | Cancelled as when the strategy cancels such an order: the cancel intent follows the submit. |
| The owner's manual order (tag `manual`) | Cancelled like any other order; this is the ADR 0027 next step, and it frees the `manual` tag. |
| An order the venue fills before the cancel arrives | Filled; the strategy reacts to the fill as usual. The request stays `Requested`, the page says the order is no longer active and points to the feed. |

### 4. The strategy's reaction: no immediate restore (superseded by ADR 0051)

ADR 0039 drains one more `Resume(Restart)` after a venue update that cancels an order "the engine did not ask to cancel", recognised as
a `Cancelled` event without a `CancelCompleted`. An owner cancel is asked for by the engine but by no block, so no `CancelCompleted`
follows it, and the resume would let a Ladder place the cancelled exit again in the same root. The rule now reads exactly as ADR 0039
words it: a `Cancelled` order that was `PendingCancel` in the last committed checkpoint was cancelled at the engine's request and does not
resume the strategy. A cancellation of an order that was working (the owner at the broker, the exchange) still resumes it. Besides the
owner's cancel, the only engine-asked cancels without a `CancelCompleted` are the orphans of a graph change (ADR 0019), whose change
already drained its own `Resume(GraphChanged)`.

The cancel is not a lasting override of the strategy, and the page says so before the owner confirms. The strategy may place the order
again by its own rules: a Ladder re-places its exit after the next averaging fill or a manual fill (ADR 0037), the Ladder SMA's averaging
order comes back at a later candle close, and every start, reconciliation that changes orders (ADR 0035), graph change and venue
cancellation (ADR 0039) resumes the strategy, whose restore wires place a missing exit again. To keep an order away, the owner stops the
bot or changes the strategy.

### 5. The page

The open orders block gets an "Отменить" button on each order at the venue, held back or being sent, while the bot runs. The owner
confirms first ("Подтвердить отмену" / "Назад"); the confirmation names the order, its side, quantity and price, says that a filled part
stays in the position, and warns that the strategy may place the order again by its own rules. After the POST the block follows the
request every second, shows "Отмена…" on the row and blocks other cancels, refreshes the open orders every second while the cancel is at
the venue, and refreshes the open orders (and so the chart's levels, which read the same query), the waits and the feed when the request
is answered. It ends with "Ордер N больше не активен" or with the rejection's reason in Russian. The manual order's confirmation no longer
says the order cannot be cancelled in ProfitGraph. The feed shows "Отмена ордера по запросу владельца".

## Consequences

- The owner cancels any working order of a running bot from the bot page on every venue, through the engine's own cancel path: one
  cancel intent, the venue's confirmation, the reservation released, no stop and no immediate restore.
- An owner cancel is not remembered by the strategy: its own rules may place the order again later, and a restart restores a Ladder's
  exit. The confirmation says so.
- A stopped bot's orders are still cancelled at the venue; the next start restores what the strategy wants (ADR 0035, ADR 0039).
- Tests: Domain transitions of `OrderCancelRequest` and `Bot.EnsureAcceptsOrderCancels`; Application handler, validator, query, expiry
  and feed tests; engine tests of `RequestOrderCancel`; an endpoint test of ownership, a stopped and a starting bot, a final, unknown and
  cancelling order and the one pending request; runtime tests on the scripted venue: a Ladder exit cancelled by the owner goes out as one
  cancel intent and is not placed again after the venue's confirmation (the test fails without the rule of section 4), a deferred submit
  is cancelled locally, and the runner rejects an expired request and one for an order that is not working; frontend tests through
  `renderApp` and the fake API. Not tested against the real venues.
