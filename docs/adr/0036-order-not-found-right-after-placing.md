# ADR 0036: An Order the Service Does Not Know Yet Is Read Again

Status: Proposed, 2026-10-28. Supplements ADR 0023; proven on the mock Invest API only. The owner reviews it in the pull request.

## Context

The T-Invest order state stream (`TInvestOrderStreams`) announces a change of an order of the bot, and the module then reads the
order's state by its request ID (`GetOrderState`, `OrderIdType.Request`) to turn the change into engine updates. After the bot placed
an order, the first announcement arrived before the order service answered for that request ID: the read returned `NotFound`
(`50005`), and the module treated it as an unresolved order (`TInvestOrderUnresolvedException("OrderNotFound")`). The exception ended
the stream, the runner failed with `RuntimeFailure` and the bot restarted by itself, after which its reconciliation found the order.

The real case (the GAZP bot, 2026-10-28 00:00–00:04 Moscow time, bot feed): every order the bot placed was followed by "stopped by
safety: execution failure, the bot will restart" (`TInvestOrderUnresolvedException`) in about 200 milliseconds: orders 4 and 5 of the
strategy and order 6, a manual market purchase that executed (30 shares) and was recorded only after the restart. The same pattern
had been in the feed of the first three orders on 2026-10-26.

## Decision

An order the stream announces and the order service does not yet know is read again, up to `MaxReads` (5) reads, `OrderReadRetryDelay`
(1 second) apart, the same retry the module already uses for a transient commission. The orders become known within moments of being
placed. Only an order that stays unknown for every read is `OrderNotFound`, as before.

The unresolved outcome stays an exception that ends the stream and restarts the runner after the retries, and it is not skipped: the
stream is the only source of the order's updates after the start's reconciliation, so a skipped announcement could lose a fill the
engine never sees. A restart reconciles with the venue again (ADR 0016), which is the safe way to recover from an update that cannot
be read.

## Consequences

- A freshly placed order no longer stops the runner: the first read that finds it applies its update.
- A fill that happens while the order is not readable is applied by the same retry; one that stays unreadable for about five seconds
  still restarts the runner and is found by the reconciliation.
- Tested on the mock Invest API (`TInvestOrderGatewayTests`): a read that answers `NotFound` once for an order that exists, then the
  fill. Not tested against the real broker. If the broker needs longer than five seconds, `MaxReads` and `OrderReadRetryDelay` are the
  places to change.
