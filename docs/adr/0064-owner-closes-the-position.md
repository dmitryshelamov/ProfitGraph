# ADR 0064: The Owner Closes the Position

Status: Accepted, 2026-10-30. The owner approved the page's mockup (a button on the position card, a confirmation, the close's
steps) and asked for it. Proven on the scripted venue, engine and domain unit tests and the frontend's fake API; nothing was
sent to a real venue.

## Context

The owner wants to leave a position at once from the bot page. A manual market sale (ADR 0046) does not do it: the strategy's
working orders, a ladder's exit and averaging order, reserve the base a sale needs (`InsufficientBase`), the strategy places
missing orders again when one is cancelled (ADR 0051), and a manual fill rebuilds a ladder's grid (ADR 0037). The kill switch
(ADR 0018) cancels the orders but never sells.

## Decision

1. **A request, taken by the bot's runner.** `POST /api/bots/{id}/position-close` with `stopAfter` stores a
   `bot_position_closes` row (migration `202610300745`) for a running bot with an open long position. A bot that does not
   run is refused (`bot_not_running`), a flat bot or one holding only a remainder no order can sell (`position_close_no_position`),
   a short (`position_close_short_unsupported`: a close would have to buy back by quantity, which owner orders do not do yet), a
   second close while one has not ended (`position_close_pending`). The runner polls for it as for a manual order; one nobody
   takes within a minute fails as `expired`. `GET` of the same path reads the bot's newest close.
2. **The engine closes.** `CausalDispatcher.BeginPositionClose` keeps a `PositionCloseState` (request id, sell order) in the
   checkpoint, asks every working order that is not already being cancelled to cancel, as an owner's cancel does (ADR 0048),
   and sets the account to reject every order except the close's own with `PositionClosing`, locally, so the strategy cannot
   place or re-place orders while the close lasts. A restored engine keeps the state and the gate.
3. **The runner advances it after every root.** While a working order is left the close waits (`Cancelling`). Then
   `SubmitPositionCloseOrder` sells the available base at the market with the reserved tag `close` and origin `$close`, which no
   graph listener of manual fills hears and the orphan sweep keeps; the account rounds it down to the step (`Selling`). A
   position already flat, or a sale the minimums refuse, leaves only a remainder no order can sell and the close ends `Closed`.
   When the sale is no longer working, the close ends `Closed` if the position is flat and `Failed` (`position_not_emptied`)
   otherwise; a sale the engine rejects for another reason ends it `Failed` with that reason. Every step is committed with its
   root (`BotRootPositionClose`).
4. **Stopping.** A closed close stops the bot when the owner asked for it (the default on the page); a failed close always stops
   it, since its orders were cancelled. A close that keeps the bot running lets the strategy act on its own rules afterwards.
5. **Feed.** `PositionCloseStarted` (orders asked to cancel, whether the bot stops) and `PositionCloseEnded` (outcome and failure
   code). Strategy orders rejected during the close appear as rejected orders with the reason «владелец закрывает позицию».
6. **Page.** The position card has «Закрыть позицию» while the bot runs with an open position. The confirmation shows the
   base sold, the price and proceeds it is valued at with the market fee, the bot's orders cancelled first and the choice
   «Остановить бота» / «Бот работает дальше»; then the close's steps, followed every second, and its outcome for half an hour.

## Consequences

- An order the venue never confirms as cancelled keeps the close waiting in `Cancelling`; the strategy stays blocked until the
  venue answers or the owner stops the bot.
- The sale is a market order: its price is the venue's, not the one shown in the confirmation.
- Closing a short is a follow-up.
