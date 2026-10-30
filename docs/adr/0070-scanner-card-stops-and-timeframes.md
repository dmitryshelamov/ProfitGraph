# ADR 0070: Scanner Card Stops Beyond the Noise

Status: Proposed, 2026-10-30. After the first live trades (most stopped out, the stops a fraction of a percent from the entry), the
owner approved stops beyond the noise for entries at the price, a buffer beyond the level and level timeframes from 15 minutes to 4
hours ("да делаем все"). The sizes of the buffer and of the minimum stop are the analyst's proposal. Builds on ADR 0059 (section 8),
ADR 0060, ADR 0061 and ADR 0065; amends ADR 0060 (section 1).

## Context

A card's stop was its level exactly: the nearest support (or resistance) more than 0.1 % from the price, a mean of nearby swing
prices. On minutes, 168 candles span under three hours and the 0.5 % merge is coarse, so the nearest level often lay within one
candle's usual move: a stop there is hit by noise, and the close-based stop sells by market after it. A level is where prices
turned, so a stop exactly at it is also where a dip that respects the level ends. ADR 0065 already leaves out a stop nearer than the
trade's fees and spread, which on a liquid coin is about 0.3 %.

On days the target of a level is weeks away while the trade has no time exit. The preset backtest measured hourly levels only
(ADR 0059, section 8, point 5).

A trade's prices are stored as `numeric(38, 18)`. A level with more than 18 decimals (a mean of thirds) was stored rounded, so the
check for a live trade with the same prices missed it and the unique index answered 500 instead of `scan_trade_live`.

## Decision

1. **The average range of a timeframe.** `ScanLevels.AverageRange`: the mean true range of the last 24 closed candles of the
   levels' timeframe, each from the previous close; empty until 25 candles exist. The cards read it with the levels
   (`ScanTimeframeLevelsReader`); the preset backtest takes the hours' range at the signal (`ScanReplay.HoursAt`).
2. **A stop beyond its level.** A stop at a support lies a quarter of the average range below it, a stop at a resistance a quarter
   above (`ScanTradeIdeas.StopBuffer`). «Цель 2R» counts its two risks from that stop.
3. **No entry at the price with a stop within one range.** A «Уровни» or «Цель 2R» card whose buffered stop is nearer the price than
   one average range (`ScanTradeIdeas.MinimumStopRanges`) is not proposed: a candle of the timeframe would reach it by its usual
   move. «Откат» keeps its stop at the hour's extreme and its rules.
4. **The same in the backtest.** `ScanStudy` applies the same `ScanTradeIdeas.LevelStop` and `LevelTarget` to the hours' levels,
   so the backtest measures the cards it shows. Without an average range (fewer than 25 candles) the stop stays at the level.
5. **Prices from levels keep eight significant digits.** A stop is rounded away from the entry, a target at a level towards it, so
   a stored trade keeps the card's prices and a second create of the same card is `scan_trade_live` again.
6. **Level timeframes 15m, 1h and 4h** (`ScanTrades:LevelTimeframes`, amends ADR 0060, section 1). The chart still offers every
   timeframe and its levels; the cards no longer come from minutes or days.

## Alternatives

- **A fixed percentage buffer.** Simple, but 0.5 % is noise on a volatile coin and a wide stop on a quiet share; a range of the
  timeframe scales with both.
- **ATR of 14 candles.** The usual indicator; 24 candles match the scanner's 24-hour windows and NATR, and the difference is small.
- **Keeping minutes and days with their own rules.** Their cards would need a backtest of their own; until the preset backtest
  replays other timeframes the cards stay where its hours are closest.

## Consequences

- Fewer cards: a pair close to its support on its timeframe offers no entry at the price there, only on a longer timeframe or as a
  pullback.
- Every stop is a little further, so a card risks the same amount with a smaller position, and «Уровни» shows a lower
  reward-to-risk.
- The preset backtest's results of the level scenarios change; earlier stored backtests keep their numbers.
