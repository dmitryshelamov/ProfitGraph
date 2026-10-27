# ADR 0034: The Operations Window of the Absence Proof Starts at the Order's Planning

Status: Proposed, 2026-10-27. Supplements ADR 0030 and ADR 0033; proven on the mock Invest API only. The owner reviews it in the
pull request.

## Context

ADR 0033 excluded the bot's own executions from the operations the absence proof reads, by matching trade numbers. It did not help
the real GAZP bot: the proof reads the broker for the trade numbers of order 1 through `GetOrderState`, and T-Invest answers
`404 Order not found` for every order of a past day, executed or not. The owner's read-only check of 2026-10-27 showed it: the
exchange order IDs of order 1 (executed), order 2 and order 3 (both cancelled) all answered 404, and `GetOrders` lists active orders
only (its `from` and `to` filters apply to orders created today). The orders-service contract has no order history, and an
`OperationItem` carries no order ID. The broker's operations are the only record of the past day, and the owner's broker screen
shows in them the executed purchase and the two orders as cancelled with nothing executed.

So the purchase of order 1, dated 18:50:05 UTC, still lay in the window of order 2 (planned 18:50:37 UTC, 32 seconds later) because
the window started five minutes before the planning (ADR 0030, ADR 0033 "Not changed"). The proof stopped on
`AutoCheck:ExecutionInOperations` although neither order executed.

## Decision

The window starts `AbsenceClockSkew` (`Venues:TInvest:AbsenceClockSkew`, default 15 seconds, at most five minutes, valid from zero)
before the attempt's `CreatedAt`, no longer five minutes. An order cannot execute before it exists, and an operation's date is the
date of the order that made it, so an operation dated earlier than the planning cannot be an execution of this order. The margin
covers only the difference between this process's clock and T-Invest's. The check, the ownership exclusion of ADR 0033 and the
position check stay as they were; an operation dated within the margin before the planning, or at any time after it, still blocks.

## Consequences

- The real case closes: the purchase dated 32 seconds before order 2 is outside its window and the two cancelled orders, which the
  broker lists, are not executions (ADR 0030), so the proof passes when the other checks hold.
- Residual risk: if this process's clock ran more than the margin behind T-Invest's, a real fill of the checked order could be dated
  before the window and be missed. The position check (the account's position equals the engine's expected one, nothing blocked) is
  the second guard and stays; the margin is an option for an owner who knows their clock is worse.
- ADR 0033's statement that the five-minute window stays is replaced by this decision; the rest of ADR 0033 stands.
- Not proven on the real broker: the mock lists operations within the request's range, as the broker does, and the tests assert the
  range and the three cases (earlier, within the margin, later).
