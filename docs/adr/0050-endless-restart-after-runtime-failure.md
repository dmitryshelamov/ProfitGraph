# ADR 0050: A Runtime Failure Restarts Without a Limit

Status: Proposed, 2026-10-28. Amends the restart rule of ADR 0016 and the status text of ADR 0029; departs from Profit Fox
Designer ADR 0054. The owner asked for it after two bots stopped for the night.

## Context

ADR 0016 restarted a bot after a `RuntimeFailure` after 30 s, 1, 2, 5 and 10 minutes, and after five restarts left it degraded
until the owner started it again. Only a root saved after a live closed candle reset the streak.

On 2026-10-27 two `1h` bots spent their five restarts within three hours while a `5m` Bybit bot with the same kind of
failures kept running:

- `TQBR:GAZP` (T-Invest): the order state stream ended with `TInvestRpcException` every 10 to 30 minutes, after the MOEX
  session had closed, so no live candle could reset the streak.
- A Steam case: reading the market-data account's wallet for the instrument rules failed with `InstrumentRulesException`.

The `5m` bot closed a candle between failures and never reached the limit. The limit therefore measured the timeframe and the
trading session rather than the health of the bot, and a passing outage at night stopped hourly bots until the morning.

A restart is safe to repeat: the runtime reconciles every order with the venue before the submit gate opens (ADR 0016), and
every condition where a repeat could do harm (an unknown submit outcome, an unresolved or foreign order, missing key
permissions, a restated history) is a safety reason that never restarts by itself.

## Decision

- **Schedule.** After a runtime failure the bot restarts after 30 seconds, then 1 minute after every further failure, without
  a limit (`BotRestartSchedule`). A runtime failure always has a restart time.
- **Counter.** `RestartAttempts` still counts restarts but stops at `BotRestartSchedule.MaxCountedAttempts` (5), so the
  `bots.restart_attempts` check stays and no migration is needed. It shows only that a streak is running; a root after a live
  candle and a start still reset it.
- **Lease.** `Bot.CanAcquireLease` lets a runtime take a degraded bot only for `RuntimeFailure`, once its restart time has
  come. A `RuntimeFailure` without a restart time, left by the former limit, is due at once, so bots stopped by it run again
  after deployment without the owner.
- **Safety and control reasons** are unchanged: they never restart by themselves.
- **Status text.** "бот ждёт владельца" after spent restarts (ADR 0029) is gone; a runtime failure always reads "сбой
  выполнения, бот перезапустится" with the time of the next restart, without the attempt number.

Keeping a longer cap, such as every 10 minutes, was considered. The owner chose 1 minute: a restart costs a few venue reads,
and an outage shorter than the delay should not keep a bot idle.

## Consequences

- A failure that never heals, such as a Steam account whose wallet cannot be read, restarts the bot every minute for as long
  as it lasts, and each failure adds a `Degraded` and a `Recovered` entry to the bot's feed. The owner stops the bot to end it.
- The venue sees the start's reads (instrument rules, open orders, balances) once a minute while the failure lasts, well
  below the rate limits of Bybit, T-Invest and Steam.
- A frequent failure no longer surfaces as a stopped bot. It is visible in the feed and in the bot's status while it is
  degraded; a stream that ends routinely still needs its own fix (ADR 0044).
