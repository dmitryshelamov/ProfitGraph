# ADR 0044: A Lost Private Order Stream Reconnects Inside the Running Bot

Status: Proposed, 2026-10-27. Amends the stream rules of ADR 0017 (Bybit) and ADR 0023 (T-Invest). The owner reviews it in
the pull request.

## Context

Each running bot has one private order stream per venue, opened by reconciliation after the venue confirmed it and before
the bot's orders are looked up (ADR 0016, ADR 0017, ADR 0023). Until now any end of that stream ended the bot's updates with
an error: `BybitPrivateStreams` and `TInvestOrderStreams` called `Fail`, the runner stored the error as a producer failure,
reported `Failed` ("A live input of the bot failed") and the bot restarted and reconciled. A plain network blip or a venue
closing an idle connection therefore counted as a bot failure, raised the restart streak and could alert the owner.

## Decision

- **Reconnect in the venue.** A lost stream is restored by the venue module that owns it, inside the running bot; the runner,
  its queue, its lease and the submit gate do not change. The outage policy is shared in Application
  (`PrivateStreamReconnectPolicy`, `PrivateStreamOutage`); the connections stay in `Venues.Bybit` and `Venues.TInvest`, which
  still know nothing of each other or of Infrastructure (ADR 0003).
- **Backoff and budget.** After a loss the venue connects again after 1, 2, 5, 10 and then every 30 seconds. When the stream
  has stayed unrestored for 5 minutes, the bot's updates end with the last loss as before, so the bot fails, restarts and
  reconciles. A restored stream that is lost again within a minute continues the same outage, so a connection that keeps
  dropping is bounded too; one that stayed up a minute starts a new outage.
- **Confirmed, then read again.** A restore counts only after the venue confirmed it: Bybit authenticated and subscribed the
  connection, T-Invest answered the subscription with `Ok` within the request timeout. Only then does the venue read again,
  as reconciliation does, what it may have missed, and only then does it handle messages again, so the ordering guarantee of
  reconciliation holds: stream first, lookups second, and a change after the lookup arrives on the stream.
  - Which orders: the orders of the bot's engine after its last committed root and of its intents whose venue state is not
    known, exactly the request reconciliation builds (`ReconcileRequests`), read through `GetBotOrdersToRefreshQuery`. An
    order placed after the read goes out after the stream was confirmed, so the stream reports it.
  - Bybit looks each order up by its client ID (open, then history orders) and publishes its state; T-Invest reads each
    attempt that may be working (`Sent`, `Active`, `Unknown`) by its request ID with `GetOrderState`, records its progress
    and publishes it. Updates are cumulative, so an order reported both by the read and by the stream is booked once.
  - An order the venue does not find is left as it is: an empty answer proves nothing (ADR 0030); the stream or the next
    reconciliation tells.
  - Foreign orders are checked again as at reconciliation (Bybit: open orders and recent executions on the symbol; T-Invest:
    active orders on the share). One that the bot does not allow ends the updates with `ForeignOrder`, so the bot degrades.
  - A read that fails, a refused subscription or a refused connection is one more failed attempt of the same outage.
- **What still ends the bot at once.** An unreadable message, a failed lookup of an order a message named (including the
  T-Invest order that stays missing for every read, ADR 0036), a foreign order and any other safety reason end the bot's
  updates on the spot, as before. Only the connection itself is retried.

A reconnect driven by the runner was considered: the runner would receive a "restored" signal and look the orders up through
the gateway from its in-memory session. It needs a new port member, a new queue root and a second ordering rule between two
channels, while the venue already holds the confirmation and the lookups; the stored checkpoint and intents describe the same
orders.

## Consequences

- A network blip no longer reports `Failed`, raises the restart streak or alerts the owner; the log shows a warning for the loss
  and an information line for the restore.
- While a stream is down, the bot keeps processing candles and its submits and cancels still go out; their updates arrive when
  the stream is restored and the orders are read again. An outage of up to 5 minutes delays updates by at most that long.
- A stream that cannot be restored within 5 minutes fails the bot exactly as before, and its restart reconciles.
- Steam has no private stream and is unchanged.
