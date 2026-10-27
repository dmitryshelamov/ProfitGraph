# ADR 0039: An Order the Venue Cancelled Is Restored, Not a Stop

Status: Proposed, 2026-10-28. Supplements ADR 0016, ADR 0023, ADR 0030, ADR 0031 and ADR 0035, and replaces the rule of ADR 0023 and
ADR 0031 that a cancellation nobody asked for during the session is `UnresolvedOrder`; follows Profit Fox (`LadderStrategy.
HandleCancelledOrder`: a cancelled exit or averaging order makes the strategy build its grid again); proven on the mock Invest API
and the scripted venue only. The owner reviews it in the pull request.

## Context

When the owner cancels the bot's exit order in the broker's app, T-Invest reports the order cancelled with nobody in the bot having
asked for it. ADR 0023 and ADR 0031 made that `UnresolvedOrder` (except the end of a DAY order's trading day, `Expired`): the bot
stopped for safety and waited for the owner's confirmation (ADR 0028). The owner asked for the Profit Fox behaviour instead: the bot
restores its own state and places the order again, and the page's block of orders to confirm is not the way to go on. Bybit and Steam
already end such an order as cancelled (`ConfirmCancel`) without stopping, but the strategy was not asked to restore anything.

## Decision

Two parts, with the responsibilities apart:

1. **The bot books the fact** (the runtime, every venue). When a venue's update cancels an order the engine did not ask to cancel (the
   engine's `Cancelled` event with no `CancelCompleted`, which only a cancel the engine asked for produces), `BotEngineSession.
   ProcessExternalUpdate` drains one more `Resume(Restart)` after the update, as ADR 0035 does after a start's reconciliation. The
   reconciliation itself passes `resumeAfterVenueCancel: false`, because it resumes once after all its updates.
2. **The strategy decides what to place.** The resume is the strategy's own re-evaluation: the Ladder revisions that restore orders on
   resume (ADR 0037's family) place the exit again when the position has base balance and no exit; the averaging order comes back at
   the next candle close (Ladder SMA) or after the next exit; a strategy without a resume wire places nothing, as before.

For T-Invest a cancelled order that nobody asked to cancel is recorded as the owner's own cancel at the broker (`TInvestCancelReason.
Owner`) when the schedule does not show the end of its trading day (`Expired`, ADR 0031, unchanged). The executions the order state
shows are booked first (the cumulative partial update the owner's cancels already produce), then the order ends as cancelled. The
stop `UnresolvedOrder` stays where the broker's state cannot be read (an order T-Invest does not know) and for an expiry that the
position does not cover (ADR 0031, ADR 0030); the owner's confirmation (ADR 0028) stays as the way out of those.

### Follow-up: an unreadable schedule is not a stop

Telling the owner's cancel from the end of the DAY order needs the trading day's end, and `TradingSchedules` answers for today and the
next week only, so it refuses the range of a day that has passed. The first version threw `TradingScheduleUnreadable` for it
(ADR 0031's "read again"), which ended the order stream and the runner: the owner's cancel of the GAZP exit at 2026-10-27 18:24 UTC
failed the bot. `Expired` now falls back as ADR 0032 does for the absence proof: the Moscow calendar ends the day of a MOEX share, and
any other exchange is not expired, so the cancellation is the owner's and the strategy restores the order. A schedule that cannot be
read never fails the bot here.

## Consequences

- Cancelling the exit or the averaging order at the broker no longer stops the bot: the exit is placed again within a moment, by the
  strategy's own rules and at the position's current average cost; it holds for every venue.
- An order cancelled by the exchange in the middle of a session for a reason of its own (a price limit, a clearing break) is treated the
  same way and restored, as in Profit Fox. If a venue keeps cancelling every order it receives, the bot places it again each time;
  the per-order limits (ADR 0018) and the venue's rate limits bound it.
- A cancellation of the owner's manual order also resumes the strategy; the strategy's guards place nothing that exists already.
- Tests: the T-Invest cases of ADR 0031 that expected a stop now expect the owner's cancel with its executions booked
  (`TInvestDayEndTests`, `TInvestCarryOverTests`); `VenueCancelRestoresOrdersTests` on the scripted venue cancels the exit and sees the
  strategy place it again with the bot still running, and fails without the resume. Not tested against the real brokers.
