# ADR 0035: A Second Resume After the Start's Reconciliation

Status: Proposed, 2026-10-27. Supplements ADR 0016, ADR 0025 and ADR 0030; proven on the scripted venue only. The owner reviews it in
the pull request.

## Context

A live bot starts in this order (`BotRunner.Run`): open the session from the stored checkpoint, which drains one
`Resume(Restart)` at the latest closed candle (`BotEngineSession.ResumeAndWarm`), then reconcile with the venue before the submit
gate opens (`BotReconciler`), and only then trade. A strategy that restores its missing orders on `OnResume` (the Ladder revisions,
`LadderResumeStrategyBlockFactory`: "restore the exit" when the position has base balance and no `ladder.exit` order) therefore ran
its resume **before** the venue answered, when the engine still held the exit and the averaging order as working.

The real case (the GAZP bot, 2026-10-27): the reconciliation proved orders 2 and 3, the exit and the averaging order, removed by
the exchange (ADR 0030, 0034) and the engine cancelled them. No resume followed. The strategy restores the exit only on a resume,
and averages only on a candle close and only while an exit exists, so the bot held 20 shares with no exit order and no averaging order
until its next start.

## Decision

After the reconciliation has applied its updates, and when it did not degrade the bot, and when any of them changed an order the
engine held (`Rejected`, `CancelConfirmed`, `PartialFill`, `FinalFill`), one more `Resume(Restart)` is drained at the latest closed
candle (`BotEngineSession.ResumeAfterReconciliation`), before the reconciliation is recorded and the gate opens. The strategy then
restores what the reconciliation took away exactly as it does after any restart; the orders it creates are intents committed while the
gate is still closed and are sent after it opens, as those of the resume at open already are. An update that only accepts an order
(`Accepted`, `LocalAccepted`) or corrects a fee changes nothing the strategy restores and causes no second resume. A reconciliation
that degrades the bot, or that the venue could not answer, causes none.

The order of a start is thus: open and warm, ask the venue, apply what it says, resume again if that changed the orders, open the
gate. The resume at open stays: it restores orders the venue does not contradict, and the indicators are warmed there.

## Consequences

- A bot whose orders the venue removed while it was stopped places them again after the reconciliation, by the strategy's own rules
  and within the bot's budget and limits; nothing is placed by the runtime itself.
- A strategy without an `OnResume` wire is not affected: its resume drains nothing.
- A fill the venue reports during the reconciliation (a position that opened while the bot was stopped) now also gives the strategy a
  resume, so an exit that was not placed is placed.
- Tested on the scripted venue (`ReconciliationTests`): a ladder with an exit and an averaging order, a restart in which the venue
  cancels both, and the new orders after it. Not tested against the real brokers.
