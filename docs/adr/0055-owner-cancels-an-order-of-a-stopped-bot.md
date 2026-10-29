# ADR 0055: The Owner Cancels an Order of a Stopped Bot

Status: Proposed, 2026-10-29. Supersedes section 2 of ADR 0048; builds on ADR 0018 (the kill's cancels), ADR 0035 (the start's
reconciliation) and ADR 0051 (an owner's cancel resumes the strategy). Proven on the scripted venue, unit tests and the frontend's fake
API only; nothing was sent to a real venue. The owner asked for it.

## Context

A stopped bot leaves its orders at the venue: a Ladder's exit and averaging order keep working while nothing in ProfitGraph follows
them. ADR 0048 refused an owner's cancel of a stopped bot's order, for three reasons: the bot has no runner and no engine in memory; the
next start's reconciliation would see the cancel as one the venue made and resume the strategy, which places the order again; and a
second path that changes an order outside the runner's queue is what that ADR avoided. The owner had to cancel at the broker, Bybit or
Steam, or start the bot and cancel from the page.

Since then ADR 0051 made every owner cancel resume the strategy: the owner cancels an order to have it gone now, and the strategy places
what it misses by its own rules. The second reason no longer differs between a running and a stopped bot. The kill switch (ADR 0018)
already cancels a stopped bot's orders without the engine: it stores one cancel intent per order, the order dispatcher sends cancel
intents of stopped bots (it holds only their submits), and the next start's reconciliation applies the venue's answer to the engine.

## Decision

### 1. Which bot takes the cancel

`POST /api/bots/{id}/open-orders/{engineOrderId}/cancel` is accepted for a running bot, as in ADR 0048, and for a stopped bot whose
runner has given the bot up: the bot should not run and nobody holds its lease (`Bot.IsStoppedWithoutRunner`). The handler locks the
lease, then the bot (the order every bot path locks them in), so a start cannot open the bot while the cancel is being stored, and a
runner that still ends its run (`desired Stopped`, lease held) refuses with `409 bot_not_running`, as do a starting and a degraded
running bot. The runtime status is not the test: a bot stopped while degraded can stay `Degraded` with no runner.

### 2. A cancel intent at once, as a kill's

For a stopped bot the handler checks the order against the last checkpoint as before (`order_not_open`, `order_cancel_pending`), then
against the stored intents:

| Order | Answer |
|---|---|
| Already has a cancel intent (a kill, an earlier cancel, the engine's own cancel before the stop) | `409 order_cancel_pending`. One cancel intent per order is also the table's unique key. |
| Its submit never left the process (pending or failed, no place started) | `409 order_not_sent`. The submit is held until the start; a cancel intent would be sent to a venue that has no such order while the submit stays queued. |
| Deferred submit (ADR 0024) | Cancel intent; the dispatcher cancels the submit locally, and the start's stored rejections end the order. |
| Sent, or its place started | Cancel intent with the submit's client order identifier; the dispatcher sends it with the usual retries and lookups. |

In the same transaction the `OrderCancelRequest` is stored already `Requested`, so the feed records `OrderCancelRequested` and the page
follows the request as for a running bot; nothing waits for a runner and nothing expires. The intent is not marked as a kill.

### 3. The open orders of a stopped bot

The engine's checkpoint does not change until the next start, so the open orders of a bot that should not run lay its cancel intents over
it: an order whose cancel the venue took (`Sent`) is no longer listed, and one whose cancel still waits or failed reads `PendingCancel`
("Отменяется"), with no cancel button. This also shows a kill's cancels. The chart's levels read the same query. A running bot's open
orders stay the engine's alone.

### 4. The next start

The start's reconciliation looks the order up as for a kill: the venue reports it cancelled (or filled, if the fill came first), the
engine ends it, and the reconciliation's single resume (ADR 0035, ADR 0051) lets the strategy place what it misses on the current
position. A Ladder places its exit again; a manual order has no block and stays gone. A failed cancel of an order still active at the
venue degrades the bot with `UnresolvedOrder`, as for a kill.

### 5. The page

The open orders block offers "Отменить" while the bot runs and once it is stopped (`desiredState` `Stopped` and the runtime neither
`Running` nor `Starting`); while it starts or still stops it says that an order can be cancelled while the bot runs or after it stops.
The confirmation of a stopped bot says the exchange is asked at once, that a filled part stays in the position, and that the strategy
places the missing orders again at the next start. The follow-up reads "Биржу попросили отменить ордер N, ждём подтверждения…" for both
modes, and `order_not_sent` reads "ордер ещё не отправлен на биржу и ждёт запуска бота".

## Consequences

- The owner cancels a stopped bot's orders from the bot page on every venue, through the same intent, dispatcher and gateway path as the
  kill switch; the owner no longer needs the broker's app to clear orders while the bot is off.
- The cancel is not a lasting override: the next start restores what the strategy wants, as after every owner cancel (ADR 0051). To keep
  an order away, the owner changes the strategy.
- A submit held by a stopped bot cannot be cancelled before the start.
- Tests: Domain tests of `Bot.EnsureAcceptsOrderCancels` and `Bot.IsStoppedWithoutRunner`; Application handler tests of a stopped bot's
  sent and deferred orders, an unsent submit, an existing cancel and a lease still held; endpoint tests of a stopping bot, a stopped
  bot's cancel answered `Requested`, a second cancel, an unsent order and the open order shown as cancelling; a runtime test on the
  scripted venue where a stopped Ladder's exit is cancelled with no runner, leaves the open orders, is recorded in the feed, and is
  placed again at the next start; frontend tests through `renderApp` and the fake API. Not tested against the real venues.
