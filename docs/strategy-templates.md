# Strategy Templates

Ready-made, editable block definitions returned by `GET /api/strategy-templates`, in this order. Each template is a
factory in `backend/src/ProfitGraph.Engine/Templates/`; its `StrategyTemplateKind` marks a strategy created from it
and selects its parameter rules (`TemplateParameterRules`), which reject a broken default and an optimization
trial whose selections break a relation. The replay engine never branches on the kind.

| Kind | Name | Idea |
|---|---|---|
| `SmaCrossover` | SMA Crossover | Buy and sell on crosses of two simple moving averages |
| `Ladder` | Ladder | Entry, averaging and exit by limit orders, with resume and the manual-fill rebuild |
| `LadderProfitFox` | Ladder (как в Profit Fox) | Ladder that raises buys to the venue's minimum notional within its limits |
| `LadderSma` | Ladder SMA | Ladder averaging from an SMA on the closed candle |
| `CapitulationDip` | Откуп капитуляции | Buy after a sharp drop on high volume; exit at a target or after a number of candles |
| `VolumeMomentum` | Импульс на объёме | Buy after a strong rising candle on high volume; exit at a target, a stop or after a number of candles |
| `LadderPlus` | Ladder+ | Profit Fox Ladder with a stop-loss from the average price and a profit target that stops new cycles |
| `DcaProfitFox` | DCA (как в Profit Fox) | Market buys at the start and every N closed candles up to a position limit; no sales |
| `Grid` | Сетка | Limit buys in steps below the start price, each with its own sale ([ADR 0058](adr/0058-grid-block.md)) |
| `EmaCrossover` | EMA Crossover | Trend: buy on a cross of a fast EMA above a slow one |
| `MacdTrend` | MACD по тренду | Trend: buy on a MACD cross above its signal line while the price is above a trend EMA |
| `RsiReversion` | Отскок RSI | Counter-trend: buy when RSI returns above an oversold level |
| `BollingerReversion` | Возврат от полосы Боллинджера | Counter-trend: buy on a close below the lower Bollinger band, sell at the middle band |
| `LevelTrade` | Сделка по уровням | One trade between given prices: a limit entry, a limit target and a stop on the close; the scanner's trade cards run it ([ADR 0060](adr/0060-scanner-trade-cards.md)) |

## Indicator templates

The four templates on the indicators of [ADR 0056](adr/0056-decimal-indicators-and-multi-output-composites.md)
([node catalog](node-catalog.md)) share one shape, so they share one editor layout with the capitulation dip:

- **Blocks.** `clock` (closed candle) → `entryConditions` (the signal, `PositionFlat`, `NoActiveOrders` and a buy size
  preview) → `entry`; `clock` → `exitConditions` (`NoActiveOrders` and a sell size preview) → `exitSignal` (any of
  the template's exit and the stop) → `exit`.
- **Orders.** Spot, long only, one position. The entry is a market buy of `buyAmount` quote; the exit is a market sale
  of the whole position. Both are decided on a closed candle and fill at the next candle's open, so the
  through-the-price rule for limit orders ([ADR 0057](adr/0057-limit-fills-through-the-price-and-capital-cost-metrics.md))
  does not touch them.
- **Stop.** Every template sells when a closed candle's close is at or below `lastBuyPrice × (1 − stopLoss)`; the
  default `stopLoss` is 5%. The stop is checked on closes, so a gap can sell well below it. No template can stay in
  a losing position without a bound.
- **Warm-up** is the engine's warm-up of the graph ([node catalog](node-catalog.md#common-rules)) with the default
  parameters; an optimization range warms up for its largest value.

| Template | Entry | Exit (besides the stop) | Defaults | Warm-up with defaults |
|---|---|---|---|---|
| `EmaCrossover` | `EMA(emaFast)` crosses above `EMA(emaSlow)` | `EMA(emaFast)` crosses below `EMA(emaSlow)` | `emaFast` 12, `emaSlow` 26, `stopLoss` 5%, `buyAmount` 100 | 27 = `emaSlow` + 1 for the cross |
| `MacdTrend` | the MACD line crosses above its signal line and the close is above `EMA(trendPeriod)` | the MACD line crosses below its signal line | `macdFast` 12, `macdSlow` 26, `macdSignal` 9, `trendPeriod` 200, `stopLoss` 5%, `buyAmount` 100 | 200 = `trendPeriod`; the MACD cross alone needs `macdSlow + macdSignal` = 35 |
| `RsiReversion` | `RSI(rsiPeriod)` above `rsiEntry` after a previous value at or below it | `RSI(rsiPeriod) ≥ rsiExit` | `rsiPeriod` 14, `rsiEntry` 30, `rsiExit` 70, `stopLoss` 5%, `buyAmount` 100 | 16 = `rsiPeriod` + 1 for RSI + 1 for the previous value |
| `BollingerReversion` | the close is below the lower band of `bandPeriod` and `bandWidth` σ | the close is at or above the middle band | `bandPeriod` 20, `bandWidth` 2, `stopLoss` 5%, `buyAmount` 100 | 20 = `bandPeriod` |

Behavior worth knowing:

- A cross fires on one candle only. After a stop the crossover templates wait for the next cross, so they do not buy
  back while the fast line is still above. The RSI template likewise needs a new return above `rsiEntry`.
- The RSI template buys the return above the level, not every candle below it: RSI falling to 0 and staying there
  does not buy.
- The Bollinger template buys on any close below the lower band while flat, so after a stop it buys again on the
  next close that is still below the band.
- The MACD trend filter compares the close with the trend EMA on the same candle; the cross itself does not need it.

Parameter rules (besides every named parameter being positive):

| Template | Rules |
|---|---|
| `EmaCrossover` | `emaFast < emaSlow`, `stopLoss < 1` |
| `MacdTrend` | `macdFast < macdSlow`, `stopLoss < 1` |
| `RsiReversion` | `rsiEntry < rsiExit < 100`, `stopLoss < 1` |
| `BollingerReversion` | `stopLoss < 1` |
| `LevelTrade` | `stopPrice < entryPrice < targetPrice` |

Periods outside 1–200 are rejected by the engine for every strategy (`UnboundedIndicatorPeriod`), so the templates do
not repeat that rule.

## Level trade

`LevelTrade` places one trade and never buys again in the same run, so a bot runs it once:

- **Entry.** On `OnStart`, with no working order and a flat position, a limit buy of `buyAmount` quote at `entryPrice`
  (tag `level.entry`).
- **Target.** When the entry fills, or is cancelled after a partial fill, a limit sale of the free base at `targetPrice`
  (`level.target`), unless the last close is already at or below the stop.
- **Stop.** On a closed candle with a position, a close at or below `stopPrice` cancels the entry and the target and sells the free
  base by market (`level.stop`); a low below the stop with a close above it keeps the position. A gap can sell well below the
  stop.
- **Expiry** is not in the graph: the scanner's tracking cancels an overdue entry through the owner's cancel (ADR 0060).
