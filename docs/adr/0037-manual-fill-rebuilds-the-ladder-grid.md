# ADR 0037: A Manual Fill Rebuilds the Ladder's Grid

Status: Proposed, 2026-10-28. Supplements ADR 0016 and ADR 0046; follows Profit Fox (`LadderStrategy.RebuildGridAfterBuy`: "a manual
market fill changes the reference for both grid legs"); proven on the scripted venue only. The owner reviews it in the pull request.

## Context

The owner buys by hand through the bot's manual order block (ADR 0016, ADR 0046): the order is a bot order with the tag `manual` and
the origin `$manual`. ADR 0046 said the strategy is not consulted by it. In the real GAZP bot (2026-10-28) a manual market buy of 30
shares raised the position to 50 shares at an average of 116.33, but the exit stayed a sale of 20 shares at 122.82 (the old position's
price) and the averaging order stayed as it was: the strategy re-prices its exit only when its own entry or averaging order fills
(`entry filled → cancelExit`, `average filled → cancelExit`), and a manual fill reaches no order node. In Profit Fox a manual
averaging buy rebuilds the whole grid: it cancels the active limit buys and the exit and builds the exit on the updated position
again. The owner wants the same here: a purchase made through the bot affects the bot.

## Decision

The resume-capable Ladder template (`LadderResumeStrategyBlockFactory`, the base of the Ladder "as in Profit Fox", Ladder SMA and
Ladder+ templates) gets an event block
`manualFilled`, an `OnOrderFilled` listener with tag `manual` (any side), and a cancel block `manualCancelAverage` for the
`ladder.average` order. Its wires: `manualFilled → manualCancelAverage`, `manualCancelAverage.completed → cancelExit` and
`manualCancelAverage.completed → restartEvent`. From `cancelExit`
the template's own wires continue as after an averaging fill: `cancelExit.completed → exitConditions → exit` (the exit on the whole
free base at the average cost of the updated position times one plus the rise), `exit.submitted → averageConditions → average` (the
averaging order by the template's own conditions and price, which depend on the last buy price or the SMA, the remaining allocation
and the cash). From `restartEvent` ("position closed?") the template's own wires start a new cycle when the owner's order closed the
whole position, as after the exit filled.

The listener is the engine's global `OnOrderFilled` node, which the engine already routes manual fills to. The averaging is cancelled by its own cancel block, not by the
template's `cancelAverage`: that block continues to the residual sale (`residualEvent → residual`, a market sale of the position), which
is right after the exit filled and wrong while the position is open.

A manual order rebuilds the grid when it is filled, whatever its side or type: the engine emits the fill event when an order is
completely filled, and a partial fill only accumulates, as with the strategy's own orders and in Profit Fox (the rebuild follows the
filled order). A manual limit order that fills in parts therefore rebuilds the grid once, at its last part, on the whole position. A
manual sale needs free base (ADR 0046), so while the exit holds all of it the owner sells only what the exit leaves free; a sale that
closes the position starts a new cycle by the template's own entry rules.

## Consequences

- The rule lives in the strategy template, not in a venue: it holds for every venue the bot runs on (Bybit, T-Invest, Steam), through
  the orders the engine already cancels and submits.
- It applies to the templates built on the resume-capable Ladder: "Ladder (как в Profit Fox)", Ladder SMA and Ladder+, new versions
  of which carry the blocks. A strategy that was saved before is brought forward by the system with an additive new version (ADR 0038);
  one the owner changed away from the template, and the plain Ladder template, are not changed.
- Backtests and replays are unaffected: a manual order exists only in a live bot, so the listener never fires there and the replay of a
  template is the same (the golden replays pass unchanged).
- Tested on the scripted venue (`ManualFillRebuildsLadderTests`): a Ladder bot with an exit and an averaging order, a manual market
  buy and its fill, and a manual limit buy filled in two parts (no rebuild at the first part), each followed by the cancels of the averaging and the exit and both orders
  placed again with the exit on the larger position; the tests fail without the wires. Not tested against the real brokers.
