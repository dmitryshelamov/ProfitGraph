# ADR 0032: Moscow Calendar as the Day-End Fallback of the Absence Proof

Status: Proposed, 2026-10-27. Supplements ADR 0030 and ADR 0031; proven on the mock Invest API only. The owner reviews it in the
pull request.

## Context

ADR 0030's proof that a DAY order T-Invest no longer finds was ended by the exchange needs "the trading day of the order has
ended, and the settle delay has passed". `TInvestAbsenceProofs.DayEnded` read that from `TradingSchedules` for the interval from
the order's last recorded instant to now. T-Invest answers that method for today and about a week ahead only (ADR 0023, Designer
ADR 0075); for a past day it returns a rejection or times out. The proof then ended with `TradingDayUnknown` and the bot stayed
in `Degraded/UnresolvedOrder` with `AutoCheck:TradingDayUnknown`. The real case: two GAZP DAY orders created on 2026-10-26
18:50 UTC, checked on 2026-10-27. ADR 0030 listed this as unverified ("if it refuses them the proof never passes"); it did.

## Decision

The proof of the day's end stays the schedule first. Only when the schedule cannot be read (a rejection, a timeout, a failed
transport, or an answer without the exchange) does the proof use a calendar fallback, and only for MOEX.

### The rule

- **Exchange whitelist.** The fallback applies when `Share.Exchange` (the `exchange` field of the share T-Invest returns) is
  `moex_mrng_evng_e_wknd_dlr`, compared case-insensitively. That is the value ADR 0023 recorded for every TQBR share seen; the
  list lives in `TInvestMoscowCalendar` and grows only by a decision that checks the new value against the broker. An empty or
  any other exchange gives `TradingDayUnknown`, as before.
- **The order's day** is the Moscow calendar date (UTC+3; Russia has had no daylight saving since 2014) of the attempt's last
  recorded instant, `UpdatedAt`, taken not earlier than `CreatedAt`. A later instant only moves the day later, so the choice is
  conservative.
- **The day has ended** when `now >= midnight Moscow after that date + AbsenceSettleDelay` (1 hour by default,
  `Venues:TInvest`). Otherwise `TradingDayNotEnded`.
- **Clock sanity.** If the order's instant is more than five minutes ahead of `now` (the proof's `Skew`), the clocks disagree and
  the result is `TradingDayUnknown`.

### Why the rule is safe

A MOEX trading day is bounded by its Moscow calendar date: the morning session opens at 06:50 and the evening session closes at
23:50 Moscow time, and the weekend session ends before midnight too (its hours were not read from the broker's documentation for this ADR; see "What is not verified"). A DAY order lives until
the end of its trading day, so it cannot outlive the midnight that closes the Moscow date it was last recorded on. The calendar
therefore gives a later bound than the schedule's true end (about 10 minutes later on a weekday), never an earlier one. Holidays
only remove sessions; they cannot extend a day. The rule needs no knowledge of which days are trading days.

### Edge cases

- **Placed in the evening close to Moscow midnight** (for example 23:58 Moscow): the date is that day, the bound is the midnight two
  minutes later, plus the settle delay. An order whose last recorded instant falls after midnight belongs to the next Moscow date and
  waits one more day. The tests pin both sides of the boundary (`now` one minute before and exactly at midnight plus the delay).
- **Holiday and weekend sessions:** the same bound; a day without sessions has no order to prove.
- **A day still running:** `now` is before the bound, so `TradingDayNotEnded`.
- **Clock disagreement:** a process clock behind the recorded instant is `TradingDayUnknown`; a clock that is only a few seconds
  ahead or behind is absorbed by the settle delay and `Skew`.
- **A readable schedule that does not show the day** (a gap, an unknown token, no day) stays `TradingDayUnknown`: the calendar is
  not a second opinion, it replaces only an unreadable schedule.

### What does not change

The other checks of ADR 0030 and the coverage guard and double confirmation of ADR 0031 are untouched: no active order, no
recorded execution, unknown by exchange order ID, no execution among the operations, equal position with nothing blocked. The calendar
decides only one of them. The proof reads and changes nothing.

### Transparency

A proof that used the calendar adds the check `TradingDayEndedByCalendar` after `TradingDayEnded`; the bot's feed words it as
"расписание биржи недоступно, конец дня определён по календарю Москвы". The log gets one information line without account data.

## What is not verified

Proven against the mock Invest API only. Not checked on the real broker: that `TradingSchedules` refuses past ranges in the way
the mock imitates (the case above shows it does not answer), that `Share.Exchange` of every TQBR share equals the whitelisted value, and
the exact close of the evening and weekend sessions (the rule depends only on their ending before Moscow midnight). The broker's
documentation was not reachable from the environment that built this change, so the reasoning rests on ADR 0023 and the
MOEX schedule the mock imitates.

## Consequences

- A bot stopped on a DAY order of a past MOEX day can return by itself in the morning, without the owner, when every other check
  passes.
- Another exchange (for example SPB) still needs the owner when the schedule is unreadable.
- Tests: `TInvestAbsenceProofTests` (a refused and a timed-out schedule with yesterday's order; the midnight boundary; an evening
  order near midnight; an order recorded after midnight; a running day; an unlisted exchange; a clock behind; the other checks
  still applying; a readable schedule keeping its behaviour) and `orderAbsence.test.ts` for the feed text.
