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
| 6. Production | Production compose, watchdog, runbook, switch-over | The owner deploys, enters credentials and starts bots | Planned |

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

Telegram bot control on top of the wave 1 alerts is also part of wave 2.

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

## Bot chart

A candle chart on the bot page ([ADR 0026](adr/0026-bot-chart.md)), for every venue.

| Step | Scope | Done when | Status |
|---|---|---|---|
| 1. Closed candles | `/api/bots/{id}/candles` from the bot's own series and market data access, pages read through the cache, incomplete venue history shown without being cached, the bot's fills of the same range; the chart pages back, marks fills and working limit orders | The chart scrolls through Bybit, T-Invest and Steam history with the bot's fills against the test venue, the mock Invest API and the mock Steam | Done |
| 2. Forming candle | A separate in-memory live channel per instrument with a closed or forming flag, throttled, never stored; history, then subscription, then reconciliation; Bybit, then T-Invest; none for Steam | The forming candle updates in its own style and becomes a closed candle without a gap | Planned |
| 3. Indicators and conditions | The strategy's indicators and node conditions in a tooltip or a panel under the chart | | Planned |
