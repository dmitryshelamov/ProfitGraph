# ADR 0060: Scanner Trade Cards

Status: Proposed, 2026-10-30. The owner asked for trade ideas on a found impulse as cards with «Создать» and «Отмена» that place
real orders with a take profit and a stop loss and follow them ("Делай карточки сразу с реальными ордерами на Bybit… Риск на
сделку задам сам"), and chose where the stop lives (point 2, "Бот-однодневка"). The remaining details are proposals reviewed in
the pull request. Builds on ADR 0016, ADR 0042, ADR 0046, ADR 0048, ADR 0055 and ADR 0059 (sections 6 and 8); it is the
semi-automatic first half of the screener robot (roadmap step 5).

The owner then set the flow (2026-10-30): the scanner catches a pair, its page shows every proposal of every timeframe at once,
the owner ticks the wanted ones and applies them, each becoming a one-trade bot with its prices ("смотрим все цели по всем ТФ и
выбираем и применяем нужные"). Levels per chart timeframe (ADR 0059, section 8, point 5) come with this change.

## Context

The scanner finds pairs and the preset backtest simulates four trade scenarios after a signal (ADR 0059, section 8). The owner
wants the next step on the pair's page: the scenarios as concrete trades for the pair now, and one click that places them with real
orders, a target and a stop, and follows them to the end. A fully automatic robot needs no click and is not built here.

ProfitGraph places real orders only through bots: an order intent, the per-order limit, the kill switch, idempotent client order
identifiers, reconciliation, fills, position cycles and Telegram notifications (ADR 0016 onwards). Bybit's spot API can attach a
take profit and a stop loss to a limit order, but its documentation does not say how they appear after the entry fills, and
following them would need a second order path beside the bots with its own reconciliation.

## Decision

### 1. A card is a scenario of the preset backtest for the pair now, on every timeframe

`ScanTradeIdeas.Of` (Engine) proposes the scenarios the backtest simulates, with the price in place of the next minute's open. The
levels come from each timeframe of `ScanTrades:LevelTimeframes` the venue offers (`1m`, `5m`, `15m`, `1h`, `4h`, `1d`): its last
168 closed candles at the snapshot price, as the pair page's levels (`ScanTimeframeLevelsReader`). Each timeframe gives its own
`Levels` and `Target2R`; the pullback comes once, from the last hour, shown under `1h`. A proposal that another timeframe already
gives with the same prices is listed once, under the shorter timeframe.

| Scenario | Entry (limit) | Stop | Target | The entry waits |
|---|---|---|---|---|
| `Levels` | the price | the support | the resistance | 1 hour |
| `Target2R` | the price | the support | two risks above the entry | 1 hour |
| `Pullback` | halfway down from the price to the hour's lowest low | that low | two risks above the entry | 4 hours |

`Levels` needs a support below and a resistance above the price, `Target2R` a support below, `Pullback` an hour that rose. The
cards are grouped by timeframe; each shows its reward-to-risk (green from 2), the amount and the loss at the stop. The owner ticks
cards and «Создать выбранные» creates a trade for each, one after another, showing the total amount and the total loss at the
stops first and each card's bot or error after; «Скрыть» only hides a card. A card whose scenario and prices a live trade already
has is marked and cannot be ticked. The backtest and the cards share the pullback rule and its wait.

### 2. «Создать» starts a one-trade bot (owner: "Бот-однодневка")

The trade runs on the bots' order path, not on a new one:

- **Strategy.** The template `LevelTrade` («Сделка по уровням»): on start, a limit buy of `buyAmount` at `entryPrice`; when the
  entry fills (or is cancelled after a partial fill), a limit sale of what it bought at `targetPrice`; when a closed candle's close
  is at or below `stopPrice`, the entry and the target are cancelled and the position is sold by market. The entry is placed only
  on `OnStart`, so the run never buys again; a restart resumes the stored run. The template is in the catalog and editable; its
  rules require `stopPrice < entryPrice < targetPrice`.
- **The owner's strategy.** The first trade creates «Сделка по уровням (сканер)» from the template; every trade's bot runs its
  latest version, so an edit of that strategy changes every live trade, like any strategy shared by bots.
- **Bot.** One bot per trade, named after the pair, the scenario and its levels' timeframe («HYPEUSDT · уровни 4h»), with the
  owner's credential, the pair, the `ScanTrades:Timeframe` candles (`1m`), the trade's prices and amount as parameters, and the
  amount as its allocation, largest order and daily loss limit, so it cannot spend more. It allows foreign orders: the owner's other
  trades on the same pair place theirs on the same symbol, which would otherwise stop it (`ForeignOrder`, ADR 0017). The trade keeps
  the timeframe its levels came from. It is started at once; a bot that does not start fails the trade and the
  error reaches the owner.
- **The stop is software.** It is checked on the closes of minute candles, so it acts up to a minute late and sells by market,
  possibly below the stop; it does not act while the Worker is down. The target rests on the venue. The owner chose this over
  Bybit's attached take profit and stop loss (section "Alternatives").
- **Real orders.** The cards trade where bots trade: real orders only with `Live:Trading:Enabled` in the Worker (ADR 0017),
  simulated otherwise. Nothing in the committed configuration changes.

### 3. Sizing (owner: "Риск на сделку задам сам")

Per owner and venue, `scan_trade_settings` keeps the credential, the risk (quote a stop may lose) and the largest amount of a
trade. The amount is `risk / ((entry − stop) / entry + 2 × fee)` with the `ScanTrades:FeeRate` of 0.1 % on each side, capped at
the largest amount, which then lowers the risk shown; it is rounded down to cents. The settings take only the owner's credential
of the scanner's venue.

### 4. Guards when a trade is created

- No settings: 409 `scan_trade_settings_missing`.
- The pair is not in the current snapshot: 409 `scan_trade_pair_not_scanned`.
- The entry is further than `ScanTrades:MaxEntryDeviation` (5 %) from the current price: 409 `scan_trade_entry_moved`; the
  card's prices are used as shown, not recomputed.
- A live trade with the same scenario and prices on the pair already exists: 409 `scan_trade_live`; a partial unique index
  enforces it. Several trades on one pair (other timeframes or scenarios) are allowed.

### 5. Following the trade

`ScanTrade` (Domain) is `Waiting` while the entry rests, `Open` while the position is held, and ends as `TookProfit`,
`StoppedOut`, `Closed` (any other sale, such as the owner's), `Expired` (the entry was not filled in time), `Cancelled` (the owner
stopped the bot before it bought) or `Failed` (the bot did not start or the venue rejected the entry).

- **Bot events** (outbox): `BotPositionOpened` opens the trade, `BotOrderFilled` of a sale tagged `level.target` or `level.stop`
  names the exit, `BotPositionClosed` closes it with the bot's realized profit. Fills, the closed position and alerts also reach
  Telegram as for every bot (ADR 0042).
- **Tracking** (`ScanTradeTrackingHost`, Worker `live` role, every 15 s): an overdue entry is cancelled through the owner cancel
  of ADR 0048 and ADR 0055; a waiting trade whose bot ran and holds neither an order nor a position ends (expired, cancelled or
  failed); a finished trade stops its bot once the bot has no working order, and is released. A stopped bot without a run is
  taken as the owner's stop only a minute after the trade was created.
- **Page.** The pair page shows the settings, the cards and the pair's trades with their status, result and bot.

## Alternatives

- **Bybit's take profit and stop loss attached to the entry.** The stop would work without the Worker and trigger on the price,
  not on a close. Rejected for now by the owner: the documentation does not describe the orders after the fill, so following them
  needs a real test order, and it is a second order path beside the bots with its own reconciliation, limits and kill switch.
- **A manual order on an existing bot.** A bot runs one strategy on one pair; a manual order (ADR 0046) has no target or stop and
  would mix the trade into another strategy's account.
- **A new bot per trade with its own strategy copy.** Avoids sharing one strategy, but fills the strategy list with copies; one
  shared strategy keeps editing in one place.

## Consequences

- Every trade is an ordinary bot on the bots page, with its chart, orders, fills, position and kill switch.
- The trades' orders are foreign to any other bot of the owner on the same pair and account; such a bot that does not allow
  foreign orders stops on them. Several trades on one pair share the venue's balance of the pair.
- Reading the cards reads up to 168 candles of six timeframes per pair through the shared candle cache.
- A trade costs a live candle subscription while it runs; the tracker stops finished bots.
- The stop can slip by up to a minute and below its price, and does not act while the Worker is down.
- On 30 days of «Импульс» every scenario lost after fees (ADR 0059, section 8); the cards place what the owner chooses, and
  the page does not advise.

## Next steps

- The same cards in the scanner's Telegram alert, with «Создать» and «Отмена» buttons.
- Short scenarios, which need a futures venue.
- Bybit's attached take profit and stop loss after a test order shows how they behave.
