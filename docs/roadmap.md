# Roadmap

Rebuild of Profit Fox Designer ([ADR 0001](adr/0001-rebuild-from-profit-fox-designer.md)). Each stage is a series of
small pull requests; a stage is done when its acceptance criterion holds.

## Wave 1: Bybit end to end

| Stage | Scope | Done when | Status |
|---|---|---|---|
| 0. Skeleton | Solution and projects, MediatR pipeline, Problem Details, health, architecture tests, CI, compose, documents; frontend skeleton | CI is green, `/api/health` responds, the frontend builds | Done |
| 1. Engine | Graph, blocks, lowering, validation, compilation, evaluation, dispatch, accounting, replay, metrics, templates | Ported tests and regression fixtures pass; templates on the same candles produce the same ledger as the MVP | Done |
| 2. Strategies and accounts | Strategy versions, block catalog API, users, sign-in, sessions, rate limits; persistence foundation, transaction behavior, migration version test | Strategy CRUD and sign-in work through API and UI | Done |
| 3. Data and backtests | Venue ports, `Venues.Bybit` public data, CSV import, background jobs, durable runs, progress, results, holdout | A template backtest on Bybit data runs from pair selection to result charts | Done |
| 4. Optimization and comparison | Grid, random, coarse-to-fine and TPE search ([ADR 0013](adr/0013-optimization.md)), walk-forward ([ADR 0014](adr/0014-walk-forward.md)), durable trials, progress over SignalR, pause and resume; ranking with filters and a side-by-side comparison of runs and trials | Walk-forward optimization completes and resumes after restart; runs and trials compare side by side | Done |
| 5. Live core | Bot and order-intent aggregates, Worker, leases and fencing, checkpoints, outbox, reconciliation, risk limits, credential encryption, Bybit order gateway (trading disabled by default), manual orders, graph change, event feed, positions, account snapshots, Telegram alerts | Bot scenarios pass against a mocked gateway; every transition has Domain tests | Done |
| 6. Production | Production compose, watchdog, runbook, switch-over | The owner deploys, enters credentials and starts bots | In progress: images, production stack, deploy job and [runbook](production.md) ([ADR 0053](adr/0053-production-deployment.md)); watchdog, backups and switch-over remain |

### Carried over from stage 1

- All deferred tests have returned: the saved Ladder SMA definition parity in stage 2, the template fingerprint tests
  in stage 3, the composite and parameter-resolution tests that use optimization search spaces in stage 4.1, and the
  walk-forward equity observer tests in stage 4.7.

## Wave 2

`Venues.TInvest` trades MOEX shares through the T-Invest Invest API ([ADR 0023](adr/0023-tinvest-moex-shares.md)):
trading sessions and closures, lots, price bands, day orders and carry-over, late commission corrections and candles
without trades. Contrary to the earlier expectation, T-Invest changes the core where ADR 0023 lists it (TC1–TC6), each
change in its own pull request with Bybit's and Steam's fingerprints and pinned replay hashes unchanged. Trading stays
disabled throughout.

| Step | Scope | Done when | Status |
|---|---|---|---|
| Model | ADR 0023 | The owner accepted the model | Done |
| Core | Market closures in history and live, candles without trades, waiting codes and fee corrections in the bot runtime, choosing a venue account for a credential, market data with the caller's credential | Each change has its tests; Bybit's and Steam's pinned values are byte-identical | Done |
| Backtests | T-Invest module and mock, credential form with account selection, share search and rules, history with a reference share, backtest UI | A Ladder backtest on minute candles of a MOEX share runs from share selection to result charts with closures, candles without trades, lots and rouble fees | Done (T6–T8, T16; proven by `TInvestBacktestTests`) |
| Live | Fee rates, account value, trading status, price band, live candles, order attempts, carry-over, late commissions, bot UI | A ladder bot passes its scenarios against the mock T-Invest: band waiting, a closed session, a day order carried over a night with a partial fill, a late commission, a restart at night and an unknown submit outcome | Done on the mock (T9–T15); trading stays disabled until the owner checks of ADR 0023 pass |
| Expired day order | The owner's absence confirmation extended to an order T-Invest accepted and then removed, with gateway checks by request ID, active orders and exchange order ID ([ADR 0028](adr/0028-confirm-order-removed-by-the-venue.md)) | A bot stopped by an expired DAY order starts again after the owner's confirmation, and never with an executed lot | Done on the mock; carry-over for a bot that is not running is closed by ADR 0030 |

Telegram bot control on top of the notifications of [ADR 0042](adr/0042-telegram-notifications.md) is also part of wave 2.

## Wave 3: Steam

`Venues.Steam` with its own model: marketplace fees, holding periods and mobile confirmations
([ADR 0022](adr/0022-steam-community-market-model.md)). The owner moved it ahead of stage 6 and wave 2. Steam changes
the core where ADR 0022 lists it (C1–C9), each change in its own pull request with Bybit's fingerprints and pinned
replay hashes unchanged. Trading stays disabled throughout; the owner turns it on with the
[Steam owner checklist](steam-owner-checklist.md).

| Step | Scope | Done when | Status |
|---|---|---|---|
| Model | ADR 0022 | The owner accepted the model | Done |
| Core | Instrument symbols with spaces, the engine fee model and settlement hold with partial fills, venue execution defaults, named credential fields, venue-owned tables, venue bot settings, account protection | Each change has its tests; Bybit's pinned values are byte-identical | Done |
| Backtests | Steam descriptor, mock Steam, account session and request scheduler, credential import and authenticator enrollment, median history, item search, backtest UI | A Ladder backtest on Steam medians runs from item selection to result charts with Steam fees and the trade hold | Done: `SteamBacktestTests` runs it against the mock Steam |
| Live | Wallet and inventory reads, live candles and display price, order journal, buy orders, listings and confirmations, history receipts, reconciliation, bot UI | A ladder bot passes its scenarios against the mock Steam: holds and unlocks, a restart during a confirmation, an expired session, a background 429, a returned item with a new ID and a CAPTCHA | Done: `SteamLadderTests` runs them against the mock Steam; no browser end-to-end test against a mock Steam yet |

## Live restart hardening

[ADR 0029](adr/0029-closed-history-after-restart-and-failure-diagnostics.md), after a Steam bot spent its five restarts on a
checkpoint check and the log showed only an exception type.

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Restated history | `RestatesClosedCandles` port capability; Steam continues on a restated last candle and records `HistoryRestated`; other venues stop with `HistoryDataCorrection` without spending restarts | `BotRuntimeTests` restart a bot on a changed last candle on both kinds of venue | Done on mocks; whether the real Steam restated the incident's hour is not proven |
| 2. Diagnostics | Allow-listed failure description in the log and the `Degraded` feed entry | The feed shows the type chain and our own message, never a foreign message | Done |
| 3. Status text | "бот ждёт владельца" once the restarts are spent | The bot page shows it from `nextRestartAt` | Done; removed by step 4 |
| 4. Endless restart | [ADR 0050](adr/0050-endless-restart-after-runtime-failure.md): a runtime failure restarts after 30 s and then every minute without a limit; the counter stops at 5; a failure left without a restart time by the former limit is due at once | Domain tests restart a bot twenty times and lease a bot stopped by the former limit; the bot page always promises a restart | Done |

## Automatic check of unresolved orders

[ADR 0030](adr/0030-automatic-check-of-unresolved-orders.md), after a T-Invest bot stopped on DAY orders the exchange removed
while its runner was down. Two pull requests: T-Invest with the shared parts first, Bybit and Steam second.

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Prevention | T-Invest carry-over for a bot nobody runs when the core authorizes it; the engine hears the recorded fills at the next start | The mock Invest API shows a failed bot's day order cancelled before the day ends and its fills replayed at the next start | Done on the mock |
| 2. T-Invest proof | Operations and positions readers, the proof of a removed order, `Bot.ClearUnresolvedOrder` and the five-minute recheck, marks on the intent, `OrderAbsenceProven` in the feed, bot page texts | The mock shows a closed order with its checks, and each failed check leaves the order to the owner with its reason | Done on the mock; not checked on the real broker (ADR 0030) |
| 3. Bybit and Steam | The safe part of ADR 0030, sections 5 and 6: a Bybit submit and a Steam buy that the venue never took, after a day and with independent evidence | The mocks show an order closed with its checks, and each failed check leaves it to the owner | Done on the mocks; the delays are not verified on the real venues |
| 4. T-Invest day end | [ADR 0031](adr/0031-tinvest-day-end-reconciliation.md): the exchange's end of a DAY order after its trading day is a lawful outcome, the final state is recorded before translation, a post-session sweep records outcomes of orders no runner reads, and a coverage guard (account position at least the bots') comes before any automatic close and the recheck | The mock Invest API shows an expiry with and without a partial fill ended in the engine, a sweep-recorded outcome accepted at the next start, a short position and a failed read leaving the order to the owner, and retries with backoff | Done on the mock; not checked on the real broker (ADR 0031) |
| 5. Calendar day end | [ADR 0032](adr/0032-moscow-calendar-day-end-for-absence-proof.md): when `TradingSchedules` cannot answer for a past day, the absence proof of a MOEX share takes the end of the Moscow calendar date plus the settle delay as the day's end | The mock shows an order of yesterday proven when the schedule is refused, the same order not proven before midnight plus the delay, and another exchange left to the owner | Done on the mock; not checked on the real broker (ADR 0032) |
| 6. Own executions in operations | [ADR 0033](adr/0033-own-executions-in-absence-proof-operations.md): an operation of the share in the proof's window is not an execution of the checked order when every trade number of it is a trade of another executed order of the same bot (read from that order's stages); anything else still blocks, and the feed names the excluded operations by engine order | The mock shows an order proven next to an earlier purchase of the bot, and an unknown, partial, unidentified, still-executing or the order's own operation leaving it to the owner | Done on the mock; not checked on the real broker (ADR 0033) |
| 7. Operations window | [ADR 0034](adr/0034-operations-window-starts-at-the-orders-planning.md): the operations the absence proof reads start 15 seconds (`AbsenceClockSkew`) before the order's planning, not five minutes, because the broker answers `404` for every order of a past day and an operation dated before an order cannot be its execution; the bot's own earlier purchase no longer blocks the proof. Proven on the mock Invest API only. |
| 8. Resume after reconciliation | [ADR 0035](adr/0035-resume-after-the-start-reconciliation.md): when the start's reconciliation cancels, rejects or fills an order the engine held, one more restart resume is drained before the gate opens, so a strategy that restores its missing orders on resume places the exit and the averaging order the venue removed. Proven on the scripted venue only. |
| 9. Order not found after placing | [ADR 0036](adr/0036-order-not-found-right-after-placing.md): the stream's read of an order the service does not know yet is retried (5 reads, 1 second apart) instead of ending the runner with `OrderNotFound`; a fresh order no longer restarts the bot. Proven on the mock Invest API only. |
| 10. Manual fill rebuilds the grid | [ADR 0037](adr/0037-manual-fill-rebuilds-the-ladder-grid.md): a complete fill of an owner's order (buy or sale; an order filled in parts rebuilds at its last part) cancels the Ladder's averaging order and exit and places them again (the exit on the whole position at the new average cost; a position closed by hand starts a new cycle), as in Profit Fox; a template change (`manualFilled` listener), so a strategy needs a new version made from the template. Proven on the scripted venue only. |
| 11. Automatic template upgrade | [ADR 0038](adr/0038-automatic-template-upgrade-of-saved-strategies.md): the Worker's `live` role saves an additive new version of every saved Ladder with resume that lacks the manual-fill reaction (blocks and wires checked, validated, idempotent, owner's concurrent save wins), so running bots follow it by ADR 0019 without a click; `Strategies:TemplateUpgrades:Enabled` switches it off. Proven on the test database only. |
| 12. Venue cancel restores orders | [ADR 0039](adr/0039-venue-cancel-restores-the-strategy-orders.md): an order the venue cancelled without the bot asking (the owner's cancel at the broker) no longer stops a T-Invest bot with `UnresolvedOrder`; the executions are booked, the order ends as cancelled and the runtime drains one more resume, so a Ladder places its exit again; for every venue. The owner's confirmation stays for orders the broker does not know. Proven on the mock Invest API and the scripted venue only. |
| 13. Ladders without resume blocks | [ADR 0040](adr/0040-upgrade-ladders-saved-without-the-resume-blocks.md): the automatic upgrade (ADR 0038) also adds the resume blocks (`resume`, `resumeExitGuard`) to a saved Ladder that lacks them (the Bybit bot's "Ladder SMA"), so an order the venue cancelled, a start or a reconciliation restores its exit; then the manual-fill reaction. Proven on unit and integration tests only. |
| 14. Ladders with their own resume | [ADR 0052](adr/0052-manual-fill-reaction-for-ladders-with-their-own-resume.md): a Ladder whose owner added an `OnResume` event under another id (the Steam bot's "CS Dreams & Nightmares Case 1h") gets the manual-fill reaction too; its own resume is kept and the template's resume blocks are not added. Proven on unit tests only. |

## Indicators

Indicator blocks for trend and counter-trend strategies, before templates on them and a real grid
([ADR 0056](adr/0056-decimal-indicators-and-multi-output-composites.md), [node catalog](node-catalog.md)).

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. EMA, RSI, Bollinger Bands, MACD | Decimal `EMA`, `RSI` and `StdDev` nodes, `Bollinger` and `MACD` composites with output ports, block catalog entries, validation of periods, `fastPeriod < slowPeriod` and a positive multiplier, editor sources | Unit tests with hand-computed values, warm-up boundaries and flat-price cases pass; a MACD crossover backtest is deterministic | Done |
| 2. Templates | Trend and counter-trend templates on these indicators, with their parameter rules ([strategy templates](strategy-templates.md)): EMA crossover, MACD with a trend EMA, RSI return above oversold, Bollinger lower band to middle, each with a stop-loss | Block layout, warm-up and parameter rule tests pass; each template trades as described on crafted candles | Done |

## Manual limit order

A limit order in the manual order block of the bot page ([ADR 0046](adr/0046-manual-limit-order.md)), for every venue.

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Limit order | `type` and `limitPrice` in the manual order request, engine limit submit, form with type, price, tick and lot hint and confirmation, rejection reasons | A limit order passes its tests on mocked gateways and the form tests | Done |
| 2. Cancel | Owner cancel of any open order of a running bot, the manual one included, as a root on the runner's queue ([ADR 0048](adr/0048-owner-cancels-an-active-order.md)): stored request, the engine's ordinary cancel intent, feed entry, confirmation and follow-up on the page; the venue's confirmation resumes the strategy, which restores its orders ([ADR 0051](adr/0051-owner-cancel-resumes-the-strategy.md)) | The scripted venue shows a Ladder exit cancelled once and placed again after the confirmation, a deferred submit cancelled locally, and the endpoint refuses other users, starting and stopping bots and final orders | Done on the scripted venue; not checked on a real venue |
| 3. Cancel of a stopped bot | Owner cancel of a stopped bot's order with no runner, as a cancel intent the dispatcher sends at once, the request answered `Requested`, the open orders following the intent ([ADR 0055](adr/0055-owner-cancels-an-order-of-a-stopped-bot.md)) | The scripted venue shows a stopped Ladder's exit cancelled with no runner and placed again at the next start; the endpoint refuses a stopping bot and an unsent submit | Done on the scripted venue; not checked on a real venue |

## Bot chart

A candle chart on the bot page ([ADR 0026](adr/0026-bot-chart.md)), for every venue.

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Closed candles | `/api/bots/{id}/candles` from the bot's own series and market data access, pages read through the cache, incomplete venue history shown without being cached, the bot's fills of the same range; the chart pages back, marks fills and working limit orders | The chart scrolls through Bybit, T-Invest and Steam history with the bot's fills against the test venue, the mock Invest API and the mock Steam | Done |
| 2. Forming candle | A separate in-memory live channel per instrument with a closed or forming flag, throttled, never stored; history, then subscription, then reconciliation; Bybit, then T-Invest; none for Steam ([ADR 0027](adr/0027-forming-candle.md)) | The forming candle updates in its own style and becomes a closed candle without a gap | Done on mocks: Bybit and T-Invest against fake transports and the mock Invest API; no real venue stream checked |
| 3. Indicators and conditions | The strategy's indicators and node conditions in a tooltip or a panel under the chart | | Planned |

## Strategy changes on live bots

A saved version that adds or removes a parameter reaches running and stopped bots ([ADR 0047](adr/0047-bot-values-follow-the-strategy-parameters.md)).

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Values follow the version | A switch fits the run's and the bot's values to the version (new parameter: its default; removed: dropped); a start with changed settings takes them on the latest version in one root; a fresh start fits too; `live-impact` returns the bots' values and the editor names the defaults they take; the feed records the fit | A running bot takes a version with a new parameter and one without it, and a stored run takes the owner's values on the latest version, on the scripted venue | Done on the scripted venue |

## Live risk controls

Corrections to [ADR 0018](adr/0018-live-risk-controls.md) found in review.

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Daily loss across midnight | [ADR 0041](adr/0041-daily-loss-across-midnight.md): the first root of a UTC day takes the previous day's last recorded equity as its baseline and is checked against it; `bots.day_last_equity` | Domain tests kill a `1d` bot and a `1h` bot on a loss across midnight; the root committer kills a bot from the stored last equity | Done |

## Outbox maintenance

Corrections to the outbox of [ADR 0016](adr/0016-live-bot-runtime.md) found in review.

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Retention and parked messages | [ADR 0045](adr/0045-outbox-retention-and-parked-messages.md): the Worker's `live` role deletes delivered messages older than 14 days in batches every hour; a message that failed 10 times is parked with its last error, logged without payload and reported by `/api/health/ready` as Degraded; `outbox_messages.parked_at` | Integration tests park a failing message with Degraded readiness and delete only expired delivered messages, batch by batch | Done |

## Sign-in and sessions

Corrections to stage 2 sign-in found in review.

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Throttling and session end | [ADR 0043](adr/0043-sign-in-throttling-and-session-end.md): only failed sign-ins count, with a lockout per login that doubles from 30 s to 15 min and ends on success, except on client addresses the login signed in from; `returnTo` accepted only on the same origin; blocking closes the account's SignalR connections and hub calls re-check the account | Integration tests lock a login out and release it, keep the owner's known address open, close a blocked account's WebSocket connections on all four hubs; unit tests reject `//x`, `/\x`, `https://x` and `javascript:` as return paths | Done |
| 2. Two-factor sign-in | Second factor for the owner | | Awaiting the owner's decision |

## Private order streams

Corrections to [ADR 0017](adr/0017-bybit-spot-order-gateway.md) and [ADR 0023](adr/0023-tinvest-moex-shares.md) found in review.

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Reconnect inside the bot | [ADR 0044](adr/0044-private-stream-reconnect.md): a lost Bybit or T-Invest private stream reconnects with backoff (1, 2, 5, 10, then 30 s) for up to 5 minutes; once the venue confirmed it, the bot's open orders are read again as at reconciliation and published; an unreadable message, a failed lookup or a foreign order still ends the bot | Fake transports and the mock Invest API: a fill missed while the stream was down is published once after the restore, a stream lost for the whole budget ends the updates, a foreign order placed meanwhile degrades | Done on mocks; no real venue stream checked |

## Bot page account summary

[ADR 0049](adr/0049-bot-page-account-summary.md): the bot page begins with the exchange account the bot trades on.

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Account card | `GET /api/bots/{id}/account` from stored data only; `IVenueAccountIdentityReader` for Bybit (`/v5/user/query-api`) and T-Invest (`GetAccounts`), cached daily in `account_identities` by the Worker; `BotAccountCard` first on the bot page with details behind a disclosure | Handler, endpoint and Worker tests pass against the test venue, the readers against the mock Bybit exchange and the mock Invest API, and the card in the app tests | Done on mocks; the real Bybit and T-Invest answers are not checked |

## Runs, optimizations and the queue

The runs page tells what the Worker is doing and finds finished work: what runs and waits in each lane, optimizations
of every strategy, and filters beyond the quote currency.

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Queue and lists | `GET /api/jobs/active` with lanes, queue places and the Worker's heartbeat; `GET /api/optimizations` with state, strategy, venue, timeframe, method and search; the run list filtered by status, strategy, venue, timeframe, search, origin, period and result bounds, with `GET /api/backtests/facets`; the header indicator, the «В работе» panel, the optimizations tab and filters kept in the address | Integration tests on the test database cover queue places across users, the heartbeat, every filter and the counts; app tests cover the panel, the indicator, the filters and the address | Done |

## Telegram notifications

[ADR 0042](adr/0042-telegram-notifications.md): a delivery queue fed by outbox handlers, a separate delivery host with
retries, an update processor with the «Принято» button, and Profit Fox's messages for lifecycle, closed positions and
filled orders.

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Decision | ADR 0042 | The owner accepted it | Proposed |
| 2. Queue and delivery | `telegram_notifications`, alert handler, `TelegramDeliveryHost` with claims, retries, age and rate limits, bot names, migration of unsent alerts | Integration tests against a fake Bot API: a 429 and a 500 are retried, a 400 fails one row without holding the next, a 403 blocks the chat, old rows are skipped | Done on the fake Bot API |
| 3. Updates | `TelegramUpdateProcessor`, message and callback handlers, `/help`, the «Принято» button | A pressed button acknowledges the owner's alert and only theirs | Done on the fake Bot API |
| 4. Trading messages | Lifecycle, closed positions, `BotOrderFilled` and the filled-order message | A partly filled order sends one message when it is filled; Engine hashes unchanged | Done on the scripted venue |

## Grid

A spot long-only grid with an honest backtest ([ADR 0057](adr/0057-limit-fills-through-the-price-and-capital-cost-metrics.md),
[ADR 0058](adr/0058-grid-block.md)), after the owner ranked work: indicators, trend and counter-trend templates, the grid, a
scanner. The edge over Bybit's free grid bots is a configurable backtest, so fill realism and the fee check come first.

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Honest fills and cost metrics | ADR 0057: `LimitFill` (`Touch`, `Through`) as a run setting with `Through` as the request default and `Touch` for stored runs; max capital deployed and committed, time in position, time under water and the longest stretch under water in every sample | Engine tests fill on a trade-through and not on a touch; pinned replay hashes are unchanged; a stored run reads as `Touch` | Done |
| 2. Grid in backtests | ADR 0058 sections 1–8: the `grid` block and `Grid` node, level state, desired-order reconciliation, upward shift, stop below the grid, cancel of a grid, the fee check and its optimization rejection, the «Сетка» template, the editor's grid inspector | A grid backtest on Bybit candles runs from template to result charts; engine tests cover partial fills, the order limit, the shift, the stop and determinism | Done: engine tests on replay and an external account; the template backtest through the API is an integration test |
| 3. Grid on live bots | ADR 0058 sections 9–10: `IVenueRestingOrderCapacity` for Bybit and the scripted venue, grid state in the checkpoint, start refusal on other venues, the fee check at start | A grid bot passes its scenarios on the scripted venue and the mock Bybit: restart with levels, owner cancel, venue cancel, partial fill, kill switch | Done on the scripted venue (`GridBotTests`: restart with levels, venue cancel, kill switch, start refused without room) and in engine tests (partial fill, owner cancel, checkpoint restore); not yet on the mock Bybit exchange |
| 4. T-Invest and Steam grids | Their own scenarios: DAY orders carried over every night, the single Steam buy slot in replay and live | | Not started |
| 5. Grid levels on the bot chart | The grid's levels and anchor on the bot chart | | Not started |

## Market scanner

[ADR 0059](adr/0059-market-scanner.md): a scanner that searches and shows, never trades; Bybit spot first, live over public
WebSockets in the Worker. After indicators, the trend and counter-trend templates and the real grid. A screener robot that
trades a strategy on every pair of a list is a later, separate product (step 5).

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Decision | ADR 0059 | The owner accepted it | Done |
| 2. Live table | `IMarketScanSource` and its Bybit implementation (universe, 1-minute backfill and stream, quote snapshot), the Worker's `scanner` role with warm-up, reconnect, gap backfill and the shared REST budget, the `worker-scanner` production service, pure metric functions, the stored current snapshot with `NOTIFY` and `/ws/scanner`, the scanner page with filters, sorting, saved and built-in presets and a link to the pair's chart | Against a Bybit stub: the table fills after warm-up, updates every minute, a dropped connection backfills its gap before showing the pair again, a new listing shows empty long-window metrics, a halted pair disappears; metric functions have unit tests on fixed candles | Done (#204, #205, #207, #210, #211, #213); runs in production since 1.0.20 (`worker-scanner`, 388 Bybit pairs). A row opens the pair's chart and metrics (#213, 1.0.25) |
| 3. Alerts | Alert switch per preset (off by default), the «Отключить» button, entry-based reporting with a cooldown, one message per preset and snapshot through the Telegram queue | Against a fake Bot API: a pair entering a preset sends one message, staying in it sends none, the cooldown holds, the button switches the preset off, a user without a chat gets nothing | Done (#215, 1.0.25): own presets only, the first check after switching on only records the result; delivery to a real Telegram chat not yet observed |
| 4. Preset backtest | What the price did after a preset's signals (owner's choice over running a strategy): 30 days of every pair's minutes replayed (`ScanReplay`), signals on entering the result with the alerts' cooldown, changes after 15 min, 1 h, 4 h and 24 h and the largest rise and drop, summary and signals on `/scanner/backtests/{id}` | The replay equals the live metric functions; against a fake source the Worker stores the signals of an invented climb; a live run on Bybit's public data completes | Done (#216, 1.0.25) |
| 5a. Levels and scenarios | Support, resistance and reward-to-risk as scanner metrics (table, presets, chart lines, alerts); the preset backtest simulates four trade scenarios after each signal (levels, levels with R:R ≥ 2, target 2R, pullback) with R after fees; up to 10,000 signals with a pair filter | The replay equals the live metrics with levels; scenario rules have unit tests on fixed minutes; a live backtest on Bybit's public data shows every scenario | Done (#218): on 30 days of «Импульс» every scenario lost after fees (mean −0.10 R for the pullback to −0.55 R for levels with R:R ≥ 2) |
| 5b. Levels on the chart's timeframe | A pair's page shows support, resistance, distances and R:R of the chart's timeframe (its last 168 closed candles at the snapshot price) next to the hourly metrics | Each timeframe has its own levels over HTTP; the page draws and lists them per timeframe | Done |
| 5c. Trade cards | On the pair's page, the scenarios as cards for the pair now on the levels of every timeframe (levels and target 2R per timeframe, pullback once) sized by the owner's risk; the owner ticks cards and «Создать выбранные» starts a one-trade bot for each of the `LevelTrade` template with real orders (limit entry, limit target, stop on minute closes), the trade follows the bot's events, and the Worker cancels overdue entries and stops finished bots ([ADR 0060](adr/0060-scanner-trade-cards.md)) | A created trade has a started bot with the card's prices and an amount that loses the risk at the stop; the trade opens, closes by its exit and stops its bot; the template's entry, target and stop have replay tests | In review; the same cards in Telegram alerts are next |
| 5d. T-Invest | MOEX shares in the scanner ([ADR 0062](adr/0062-tinvest-market-scanner.md)): `IMarketScanSource` on the Invest API with the scanner's configured T-Invest credential (`Venues:TInvest:Scanner:CredentialId`), closed minutes over the market data stream with pings as liveness, its request and subscription budgets, rouble presets, the venue's currency on the page; the volume spike's baseline counts only hours with trades | Against the Invest API mock: universe, candles in shares with turnover, weekend sessions left out, the rate-limit pause, closed minutes and pings over the stream, a refused subscription; the runner keeps a silent stream that reports itself alive; the replay equals the live metrics on a market with nights and weekends | Done (#221); a live run on the real Invest API waits for the owner's permission |
| 5e. Futures and shorts | Bybit USDT perpetuals as venue `bybit-linear` ([ADR 0061](adr/0061-bybit-futures-and-short-trades.md)): market data, instruments, fees and the scanner on the linear category, the order gateway in hedge mode (reduce-only exits, the bot's leverage set before an entry, one-way mode stops the bot, funding is no foreign order); short runs on a margin account in the engine with the bot's leverage, cover nodes and the `LevelTradeShort` template; short cards and short scenarios in the preset backtest on futures | Engine replay and external-account tests for shorts and leverage; the gateway against the mock Bybit exchange; a short card over HTTP creates a started bot of the short strategy at the settings' leverage and closes by a buy at the target; the page shows long and short cards | In review; no real futures order sent yet (needs hedge mode and the owner's go-ahead) |
| 5. Screener robot | After a preset's signal, a scenario per pair: a pullback zone, a stop, targets and an expiry, a confirming closed candle, then one entry signal (or a trade) per scenario; the levels come from rules, not from hand-picked prices | Rules chosen from the preset backtest's numbers and tested on history before any live use | Planned; design discussion first |
