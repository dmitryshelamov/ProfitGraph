# ADR 0029: Closed History After a Restart, and Failure Diagnostics

Status: Proposed, 2026-10-27. Written after a live incident; the owner reviews it in the pull request.

## Context

A Steam bot ("CS Dreams & Nightmares Case 1h") failed with `RuntimeFailure` at every start and spent all five automatic
restarts (30 s, 1, 2, 5 and 10 minutes) within minutes. The Worker log held only the exception type. Three things made it
worse than it had to be:

1. `BotEngineSessionFactory.Open` restores a stored run on the closed history that ends at the checkpoint's last candle, and
   throws `InvalidOperationException("Closed history cannot restore the bot's checkpoint.")` when the re-read candle is not
   equal to the stored one. A thrown exception is a `RuntimeFailure`, so the bot retried the same comparison five times.
2. `BotRunner` logged `Bot {BotId} failed with {ExceptionType}`; the message, which here said exactly what was wrong, was lost.
3. The bot page said "сбой выполнения, бот перезапустится" also after the fifth restart, when the bot already waits for the
   owner.

## Findings

**The hypothesis is confirmed by the code, not by Steam.** The candle the bot stored came from `SteamLiveCandleSource`, which
reads the item's whole `pricehistory` 120 s after the hour closed (`CandleCloseDelay`) and yields the candle of that hour. The
restart re-reads the whole history through `SteamHistoricalCandleSource` and `SteamPriceHistoryParser` builds the candle again
from the payload of that moment: median price and count of sold items of that hour. Nothing is stored in between (the
in-memory `HistoryReuse` is ten minutes), and `Candle` equality compares every field, so a late sale or a corrected median of
the closed hour makes the two candles different. `SteamMarketDataTests.ALaterReadRestatesAClosedHourAndTheSourceSaysSo`
shows it with the mock Steam: the same hour read after a change of the payload is another candle. What is **not** proven: that
Steam restated the hour of the owner's bot. That needs the real payloads of the incident, which agents do not read, and the
log of the incident has no message. The new diagnostics below will name the cause the next time.

**Bybit's restart counter is not a defect.** A root saved after a live closed candle resets `RestartAttempts` and
`NextRestartAt` in the same transaction (`BotEngineSession.Save` with `liveCandle: true`, ADR 0016); `BotRuntimeTests.ALiveCandleAfterRestartsStartsTheRestartStreakOver`
proves it through the Worker: a bot with four counted restarts returns to zero after one live candle. A counter that reached
five on five-minute candles therefore needs five failures in a row with no live candle committed between them, each run dying
before its first live candle (while opening, reconciling or subscribing), which the schedule makes likely only when the cause
is persistent for about 18 minutes (a venue or network outage, a rejected key). The cause itself cannot be read from the old
log. No code change in this part.

## Decision

### 1. A restated last candle

A venue declares in a port capability whether it recomputes closed candles: `IHistoricalCandleSource.RestatesClosedCandles`
(default `false`; `true` for Steam only). `Open` then compares the stored last candle with the re-read one:

| Re-read history | Venue restates | Result |
|---|---|---|
| ends at the stored candle, equal | any | restore as before |
| ends at the stored candle, different | yes (Steam) | **accept**: the checkpoint's last candle becomes the re-read one, the first commit of the run records the bot feed entry `HistoryRestated` (open time, previous and restated close) |
| ends at the stored candle, different | no (Bybit, T-Invest) | the bot degrades with the safety reason `HistoryDataCorrection` |
| does not contain the stored candle | any | the bot degrades with `HistoryDataCorrection` |

Why Steam accepts. Its candles are medians that the venue recomputes, so a difference is the normal state of the data, not a
sign of an unsafe one; stopping the bot would make every restart after a late sale a case for the owner, and the earlier
candles of the warm-up window are restated and accepted silently today anyway. Accepting changes what the engine knows, not
what it did: orders and fills come from the checkpoint's account, and the indicators are rebuilt from closed candles at every
restore (`GraphEngineCheckpoint` holds no indicator samples). An order placed on the old value stays what it was. The record in
the feed shows the owner that it happened.

Why the others stop. Closed candles of Bybit and T-Invest are final; a different one means a correction of history (the same
reasoning as T-Invest's existing `HistoryDataCorrection` for trades in an interval that was received empty, ADR 0023), and a
decision made on the old value may have to be reviewed. A safety reason does not restart by itself: `BotRunner` reports
`Degraded` with the reason instead of `Failed`, so no automatic restart is spent and the owner starts the bot after looking.
`HistoryDataCorrection` is already a safety reason; its text on the bot page now says that the bot waits for the owner.

Backtests and replay are unchanged: they read history once, never restore a checkpoint, and `Engine` is untouched, so
deterministic results and the pinned replay hashes stay as they are.

### 2. Failure diagnostics

`BotRunner` logs and reports a short description of a failure from `BotFailureDiagnostics.Describe`, and the bot's
`Degraded` feed entry carries it as `detail` (one line, at most 300 characters; `BotDegraded` has an optional `Detail`, stored
events without it still read). The text is built by an allow-list:

- the message is included only for exceptions this application wrote: `OrderGatewaySafetyException`, Domain exceptions, and an
  `InvalidOperationException` or `ArgumentException` whose throwing method belongs to a `ProfitGraph.*` assembly;
- every other exception (framework, database driver, HTTP client, a venue's own exception types) is reduced to its type name,
  because its message may carry an address, account data or the venue's raw answer;
- up to three levels of `InnerException` are described the same way, so `A live input of the bot failed. <- InvalidDataException`
  keeps the wrapper's message and names the cause by type;
- a run of 20 or more token-like characters in an included message is masked as `***`, so that a text that slipped through still
  holds no key.

The stack trace is not logged. A stable code was considered and rejected for now: the open-ended exception set has no codes
today, and the type chain plus an authored message answers the question that was asked.

### 3. The status text

`RuntimeFailure` with no scheduled restart (`nextRestartAt` is null, which is exactly "five restarts spent") shows "сбой
выполнения, бот ждёт владельца" and a line on the bot page; with a scheduled restart the text is unchanged. The API already
returns `restartAttempts` and `nextRestartAt`, so no contract change is needed; the feed kind `HistoryRestated` is added to the
`BotFeedKind` enum of the contract.

Superseded by [ADR 0050](0050-endless-restart-after-runtime-failure.md): restarts no longer run out, so the text and the line
are gone.

## Consequences

- A Steam bot continues after a restatement of its last hour; the owner sees it in the feed.
- A bot on a venue with final candles stops once, with a reason and without wasting restarts, when its history is corrected.
- A failure's cause is in the log and the feed for our own errors; the cost is that foreign exceptions stay type-only.
- Not checked on a real venue: whether Steam restates the stored hour, and what the real payload looked like. The checks are
  mocks and unit tests only. After the first restart of a Steam bot on this build, read the `HistoryRestated` entry or the
  `detail` of the `Degraded` entry.
