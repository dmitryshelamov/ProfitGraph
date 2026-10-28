# ADR 0051: The Owner's Cancel Resumes the Strategy

Status: Proposed, 2026-10-28. Supersedes section 4 of ADR 0048; returns to the rule of ADR 0039 as it was before ADR 0048. The owner
asked for it after a Steam Ladder bot was left with a position and no orders. Proven on the scripted venue only.

## Context

ADR 0048 let the owner cancel one working order of a running bot from the bot page, and decided that the venue's confirmation of such a
cancel does not resume the strategy: a `Cancelled` order that was `PendingCancel` in the last committed checkpoint was excluded from the
resume of ADR 0039, so a Ladder would not place the cancelled exit again at once. The order came back only at the next start,
reconciliation that changed orders, graph change or venue cancellation.

On 2026-10-28 the owner cancelled the averaging order and then the exit of the Steam bot "CS Dreams & Nightmares Case 1h" from the page.
The averaging order was in the way of a manual buy (Steam keeps one buy order per item), and the exit was sized for three items while the
bot held four after the manual buy. Neither came back: the bot held four items with no exit and no averaging order, and nothing in its
graph would place them before a restart. The owner cancels an order from the page to have the strategy place it again with the current
position, not to keep it away; to keep orders away the owner stops the bot.

## Decision

- A venue update that cancels an order without a `CancelCompleted` (no block asked for the cancel) drains one more `Resume(Restart)`,
  whoever asked: the venue, the owner at the venue, or the owner on the bot page. The exclusion of orders that were `PendingCancel` in the
  last checkpoint is removed. A strategy's own cancel still completes with `CancelCompleted`, and its block continues the strategy, so it
  does not resume.
- The resume runs in the root of the venue's confirmation, so the cancelled order is no longer working when the strategy's guards look:
  a Ladder places its exit again on the whole base balance at its current exit price, and the averaging order follows the exit by its
  conditions. A deferred submit cancelled locally (ADR 0024) is confirmed the same way and resumes too.
- The orphans of a graph change (ADR 0019) also resume once more when their cancels are confirmed, as under ADR 0039. Their change drained
  `Resume(GraphChanged)` already; a second resume finds the strategy's orders in place and its guards do nothing.
- The confirmation on the page says that the strategy places the missing orders again by its own rules once the venue confirms the cancel.
- The start's reconciliation keeps its own single resume (ADR 0035): it still applies its updates without the per-update resume.

## Consequences

- Cancelling an order on the page refreshes it: the Ladder's exit comes back sized to the current position. An owner who wants an order
  gone stops the bot or changes the strategy.
- The owner's cancel of a manual order also resumes the strategy; a manual order has no block, so nothing places it again.
- Tested: `OwnerOrderCancelTests` on the scripted venue show the Ladder exit cancelled by the owner going out as one cancel intent and
  placed again after the venue's confirmation; the deferred submit is still cancelled locally. Not tested against a real venue.
