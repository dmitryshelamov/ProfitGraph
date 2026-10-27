# ADR 0028: Confirming an Order the Venue Removed

Status: Accepted, 2026-10-27. Built with this pull request; proven on the mock Invest API only.

## Context

A T-Invest bot stopped in `Degraded/UnresolvedOrder` and could not start again. Two limit DAY orders were `Active` in
`tinvest_order_attempts` on 2026-10-26. The bot then failed with `RuntimeFailure` and did not run when the trading day
ended, so the carry-over of ADR 0023 (T12b) never cancelled them; the exchange removed them itself. At the next start the
reconciliation read each order by its request ID, T-Invest answered `NotFound`, and `TInvestOrderGateway.LookUp` returned
`UnresolvedOrder`. Every start repeated this.

The existing way out, the owner's confirmation that an order is absent (Profit Fox Designer ADR 0055; `OrderAbsence`,
`ConfirmOrderAbsentCommand`), covers only a submit that the venue never confirmed (`PendingSubmit`, intent `Pending` or
`Failed`) on a venue whose descriptor sets `ConfirmsOrderAbsence`. T-Invest does not, and an order it accepted (`Active`,
intent `Sent`) is outside the rule, so the owner had no lawful way out. ADR 0023 says a cancellation nobody asked for is
`UnresolvedOrder`; it did not say how the owner ends that.

A guess that the order is gone is not enough: an order that T-Invest does not find by one key may still be working or may
have been filled, and cancelling it in the engine would leave the bot with a position or a reservation it does not know.

## Decision

One confirmation, not a second mechanism: the owner's absence confirmation is extended to an order the venue accepted. The
same command (`POST /api/bots/{id}/order-intents/{intentId}/confirm-absent`), the same `confirmed_absent_at/by` columns on
the intent, the same `OrderIntentAbsenceConfirmed` event and feed entry, the same "unresolved orders" list and the same
`OwnerConfirmedAbsent` flag that reconciliation passes to the gateway. A venue opts in with its own flag,
`IVenueDescriptor.ConfirmsExpiredOrders` (T-Invest only), because for T-Invest the `PendingSubmit` rule is decided by the
attempts table and must not change.

### When the owner may confirm

All of these hold (`OrderAbsence.IsAcceptedAndStopped`, `CanConfirm`):

- the venue sets `ConfirmsExpiredOrders`;
- the bot is `Degraded` with `UnresolvedOrder`;
- the intent is a delivered submit (`Sent`, place started) that was not confirmed before;
- the engine order is `Active` or `PendingCancel`;
- the engine saw no execution of it (`GrossBase` of its progress is zero). A partly filled order is never confirmed: its
  remainder and its fills must be accounted by the venue's state or by the owner outside the bot.

The aggregate method is `OrderIntent.ConfirmExpired`: a `Sent` submit gets `confirmed_absent_at/by` and stays `Sent`;
nothing is sent again. `ConfirmAbsent` is unchanged.

### What the gateway checks on the next start

The confirmation changes nothing by itself; it is an input to reconciliation (`ReconcileOrderInternal.OwnerConfirmedAbsent`,
now also set for a confirmed `Sent` intent). `TInvestOrderGateway.LookUp` first reads the attempt by its request ID as
before. Only when that answer is `NotFound`, the owner confirmed, and the attempt is `Active` with an exchange order ID, it
proves the removal (`ProvenRemoved`):

1. no active order of the account on the share carries the attempt's request ID or its exchange order ID;
2. `GetOrderState` by the exchange order ID (`OrderIdType.Exchange`) also answers `NotFound`;
3. no attempt of the engine order, including earlier carried-over ones, executed any lot.

If all hold, the attempt becomes `Final` with `cancelled_by = Owner` (nothing executed) and the gateway reports
`ConfirmCancel` for the engine order: the engine cancels it as an owner cancel, strategies hear `Cancelled`, the reservation
is released, and the bot starts. If any check fails or a read errors, the order stays unresolved and the bot stays
degraded. If T-Invest still knows the order under either key, the normal path applies and the venue's state decides.

### What is not checked

The module has no reader of positions or operations (only `GetPortfolio` for the account value). So the venue cannot rule
out an execution the stream and `GetOrderState` never reported, for example a fill of the order that the engine missed while
the bot was down. That is the owner's part: the bot page says to confirm only after checking, in the broker's app, that the
order is not active and that operations and the position show no execution, even a partial one. A position and operations
check in the gateway is a later improvement; it needs the operations methods, their request limits and an agreed rule for
matching an operation to an order.

### Interface

`GET .../unresolved-orders` lists such an order while the conditions above hold, with `acceptedByVenue = true`, so the page
words it as "the order was removed by the venue, nothing was executed" and not as "the order never arrived". An accepted
order with an execution is listed with `canConfirmAbsent = false` and the page says to check the execution at the broker.
The restart that follows the confirmation is the owner's, as for a submit confirmed absent. No new endpoint, no migration.

### Why carry-over did not run (finding)

`TInvestWaitingOrdersWorker.CarryOver` considers only a bot whose runner reads the bot's order updates in this process
(`streams.IsOpen(botId)`), because the result of the cancel is published to the engine through that stream and the
runner. When the bot fails with `RuntimeFailure`, the runner stops, `TInvestOrderGateway.Updates` closes the stream, and the
worker skips the bot even close to the end of the session. So an order of a failed or stopped bot is not carried over; the
exchange removes it. This is intended by ADR 0023 ("a stopped, killed or degraded bot never sends because a band moved or a
session opened") for sending, and the same safety applies to cancelling: carrying over for a bot nobody runs would cancel
real orders the owner may have stopped on purpose, and the remainder would then wait for a bot that is not running.

It is not a trivial defect, so it is not changed here. Open options for a later ADR: an alert that a bot with working DAY
orders is not running some minutes before the day's end; or a carry-over for a degraded bot whose desired state is
`Running`, recorded as `cancelled_by = CarryOver` so that the next reconciliation continues the remainder (the data path
already supports this, since reconciliation applies a cancelled carried attempt through `TInvestOrderProgress`). The cause of
the original `RuntimeFailure` is a separate matter.

## Consequences

- An expired DAY order no longer needs a manual database edit: the owner checks the broker app, confirms, restarts.
- Safety relies on three machine checks and one human check. Nothing here lets an agent place, amend or cancel a real order:
  the confirmation is an owner's decision, and the gateway only reads.
- The bot still degrades when a DAY order expires while the bot is down; this ADR gives the way out, not the prevention.
- Tests: Domain (`ConfirmExpired`), Application (`ConfirmExpiredOrderTests`), the gateway on the mock Invest API (removal
  proven; kept by the exchange order ID; an earlier attempt with executed lots) and the frontend page.
