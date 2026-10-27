# ADR 0031: T-Invest Day-End Reconciliation

Status: Proposed, 2026-10-27. Built in one pull request on top of ADR 0030; proven on the mock Invest API only. The owner
reviews it in the pull request.

## Context

ADR 0030 keeps a failed bot's DAY orders from expiring (offline carry-over) and closes an order T-Invest no longer knows when
the broker's records prove that nothing executed. Three gaps remain, found by comparing with the owner's previous project
profit-fox (its trading session store and order recovery):

1. **The outcome is lost while T-Invest still knows it.** A DAY order of a bot that is stopped, degraded for a safety reason or
   never started is not carried over (ADR 0030 keeps such orders as they are). The exchange ends it with the day, and at the next
   start `GetOrderState` answers `NotFound`. Only a partly filled order is then left to the owner, because ADR 0030 cannot prove a
   partial fill. Yet T-Invest answered for the order the whole evening after the day ended, with its final state and executions.
2. **The exchange's own end of a DAY order is not a lawful outcome.** ADR 0023 says a cancellation nobody asked for is
   `UnresolvedOrder`. `TInvestOrderTranslation` threw for it before `TInvestOrderProgress` recorded anything, so not even the final
   state and the executed lots reached the attempts table. profit-fox treats the exchange's `Cancelled` as an ordinary final state
   with idempotent fills.
3. **No guard compares the account with all its bots.** ADR 0030's position check is equality with one bot's engine, which a
   share the owner holds outside the bots fails. profit-fox's rule is coverage: the broker's position must be at least what the
   bots hold.

## Decision

### 1. An exchange cancellation after the day's end is a lawful outcome

This changes ADR 0023's rule for one case. A DAY limit attempt that nobody asked to cancel (`cancelled_by` empty) and that
T-Invest reports `Cancelled` when the trading day holding its last recorded instant has ended by the schedule
(`TradingSchedules`, the vocabulary of ADR 0023) is recorded with the new reason `cancelled_by = Expired`. Translation treats it
as the owner's cancel: the executions of every attempt of the engine order arrive as one cumulative `Partial`, the remainder is
cancelled (`ConfirmCancel`) and strategies hear `Cancelled`. Nothing is sent and nothing is placed again.

Everything else stays unresolved:

- a cancellation read while the day has not ended (during the session);
- a day outside the vocabulary, a gap between days, or no day: the schedule does not know, so the module does not guess;
- a market order: it is not a DAY order.

A schedule that cannot be read leaves the state unrecorded and the read is tried again (`TradingScheduleUnreadable`, a transient
reason), so a passing outage never records an expiry as a cancellation nobody asked for.

T-Invest's `OrderState` carries no cancellation time, so "after the day ended" means "read after the day ended". A cancellation
nobody asked for during the session that is first read after the day ended (the bot was down all evening) is taken for the
exchange's own. It is still a final state from T-Invest with exact executions, so the accounting stays right; what is lost is the
stop that would have shown the owner something unusual happened. The coverage guard below and the feed entry are the answer to
that.

### 2. The final state is recorded before it is translated

`TInvestOrderProgress.Apply` now records the state, the exchange order ID and, for a final state, the attempt's own executed lots,
quote and fee, and only then translates it. A translation that stops the bot (a cancellation nobody asked for, a rejection after
fills) no longer loses the outcome. A recorded final attempt that T-Invest no longer answers for is read from the table at the next
reconciliation:

| Recorded | T-Invest answers `NotFound` |
|---|---|
| all lots executed (ADR 0030) | the recorded fill ends the order |
| `Expired` | `Partial` with the recorded executions and `ConfirmCancel`, after the coverage guard |
| `Owner` | `Partial` with the recorded executions and `ConfirmCancel` |
| `cancelled_by` empty (a cancellation nobody asked for) | unresolved; the owner's confirmation (ADR 0028) closes it when no attempt of the order executed anything |

### 3. The post-session sweep

`TInvestClosedSessionSweep` (Worker, live role, with T-Invest trading) records outcomes while T-Invest still knows them:

- It considers every `Active` DAY limit attempt of a bot whose order stream is not open in this process: degraded, failed,
  stopped or never started. A running bot hears its orders through its stream.
- As soon as the trading day holding the attempt has ended by the schedule, and then every `ClosedSessionSweepInterval`
  (5 minutes, `Venues:TInvest`) until the next session opens, it reads `GetOrderState` by the request ID and records a final
  state with section 2's rules, `Expired` included. A carry-over that was marked but not read back is continued as before.
- It only reads: no `PostOrder`, no `CancelOrder`, nothing reaches the engine. The next reconciliation takes the recorded outcome.
- A day the schedule does not know is left alone; an order T-Invest no longer answers for is left to ADR 0030's proof; after the
  next session opens the sweep stops reading that attempt.

Reads are grouped by account (token and account ID). An account whose sweep cannot finish, because `GetOrderState` failed or the
schedule could not be read, is tried again after 5 seconds, doubling up to one minute, at most six times; then it waits for its
next scheduled sweep. Calls go through the token's `Orders` and `Instruments` request limits (`TInvestRequestLimiter`).

### 4. Coverage guard

Before the module ends an order as `Expired` by itself (on the stream, at reconciliation and in the recheck below), the account
must cover its bots (`TInvestPositionCoverage`):

- the account's position in the share from `OperationsService/GetPositions`, free plus blocked, in shares (the `Operations`
  request limit);
- must be at least the sum of what every bot on the same account and share holds by its last checkpoint (`BaseTotal`, in shares:
  lots times the lot), plus what the order being closed adds that its bot has not booked yet. Reconciliation knows the booked part
  (`ReconcileOrderInternal.ExecutedBase`); on the stream it is unknown and the stricter assumption is used (a buy unbooked, a sell
  booked).

Bots on the same account are found through the core (`GetVenueBotPositionsQuery`, every bot of the venue in any state) and each
bot's own credential, so bots under different tokens of one account are added up. It is not an equality: the owner may hold shares
of their own. A shortfall (`PositionShort`), a position the broker cannot give now (`PositionUnreadable`), a failed read
(`ReadFailed`) or bots that cannot all be added up (`BotPositionsUnknown`) leave the order unresolved; the stop says which
(`AutoCheck:<code>`, as in ADR 0030). The guard is insurance against a wrong close, not proof that nothing else executed.

### 5. The stopped bot comes back by itself

ADR 0030's five-minute recheck (`RecheckUnresolvedOrdersCommand`, `IOrderGateway.CanProveAbsence`) now also counts an attempt
that would end as `Expired` (read from T-Invest or recorded by the sweep) and an attempt recorded as the owner's cancel, each
after the coverage guard for an expiry. When every order the bot could not establish would end so, `Bot.ClearUnresolvedOrder`
returns the bot to `Created` with `BotRecovered`, and its own reconciliation ends the orders before the submit gate opens. The
transition is ADR 0030's; no new one is needed. A bot the owner stopped is never touched.

This is the narrow exception to "a safety reason never clears itself" (`BotStatusReasons`): `UnresolvedOrder` clears only
through `ClearUnresolvedOrder`, only for a bot whose desired state is `Running`, and only when the venue's own evidence ends every
such order; `VenueAccountProtection` clears through `ClearVenueAccountProtection` when the venue reports the account protection
closed (ADR 0016, ADR 0022). Every other safety reason still needs the owner.

### 6. Transparency

A reconciliation that ends an order as `Expired` attaches the checks `CancelledByExchange`, `TradingDayEnded` and
`PositionCoversBots`. As in ADR 0030, the intent is marked as closed automatically and the bot's feed lists the checks in words.
The bot page words the new failed checks.

## Alternatives

- **Infer expiry from the clock without T-Invest's state.** Rejected: profit-fox's rule that a DAY order belongs to the exchange
  until an update or a reconciliation says otherwise stands; a clock is a precondition, never evidence.
- **Equality of the account's and the bots' positions.** Rejected: any share the owner holds outside the bots would block every
  automatic close (profit-fox's drift check fails on it).
- **Carry over the orders of every bot, stopped ones included.** Rejected by ADR 0028 and ADR 0030: a stopped bot's orders may be
  left on purpose; cancelling them is the owner's call.

## What is not verified

Proven against the mock Invest API only. Not checked on the real broker: that a DAY order ended by the exchange reads as
`EXECUTION_REPORT_STATUS_CANCELLED` with its executed lots and stages after the day's end, and for how long `GetOrderState` keeps
answering for it; that `GetPositions` counts shares and not lots (ADR 0030 has the same open question); that `TradingSchedules`
answers for past days. If T-Invest forgets the order before the sweep reads it, ADR 0030's proof or the owner decides, as before.

## Consequences

- A DAY order of a bot nobody runs keeps its outcome, including a partial fill, and the bot ends it at its next start.
- A bot stopped on such an order returns by itself within five minutes when the account covers its bots, and stays with the owner
  when it does not, with the reason on the bot page.
- `cancelled_by` accepts `Expired` (migration `202610270101`). `TInvestOptions.ClosedSessionSweepInterval` is new.
- Tests: the gateway, the stream and the sweep on the mock Invest API (`TInvestDayEndTests`: an expiry with nothing executed and
  with a partial fill; a cancellation during the session stays unresolved with its outcome recorded; an unknown schedule; a short,
  unreadable or unknown position blocks; the bots of the account are added up; a failed read is retried with backoff; an outcome
  the sweep recorded is accepted at the next start; the owner's confirmation still ends a recorded cancellation nobody asked for),
  Application (`VenueBotPositionsTests`), persistence (`Expired` and `ListOnVenue`), and the frontend texts.
