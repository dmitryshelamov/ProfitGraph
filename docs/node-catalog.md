# Node Catalog: Indicators

The strategy engine's indicator nodes, their formulas, warm-up and edge cases. Blocks reach them as value sources of the
block catalog (`GET /api/block-catalog`); the lowerer turns a value expression of that kind into the node of
the same key. Decisions behind this page: [ADR 0056](adr/0056-decimal-indicators-and-multi-output-composites.md).
Templates built on these indicators: [strategy templates](strategy-templates.md).

## Common rules

- **Clock.** An indicator advances exactly once per closed candle, warm-up included. Reads on other events (fills,
  cancellations, intrabar phases) see the value of the last closed candle and never advance it. Nothing looks ahead:
  the value on a candle uses that candle and earlier ones only.
- **Input.** Every indicator except ATR takes one numeric series `in` (`Integer`, `Scalar`, `Price`, `BaseAmount`,
  `QuoteAmount`) that must come from the closed-candle clock: a candle field, another indicator, `previous`, or
  arithmetic over them. The output keeps the input's unit (an `Integer` input gives `Scalar`); RSI is always `Scalar`.
- **Periods** are integers from 1 through 200: a literal, an optimization range or a wired parameter with a static
  bound. The engine rejects anything else with `UnboundedIndicatorPeriod` before any replay.
- **Availability.** A node is unavailable until it is warmed up; `IsAvailable` tells a warming indicator from a real
  zero. An unavailable sample (a warming input) restarts the indicator, which then needs its whole warm-up again.
- **Warm-up of a strategy** is the largest demand over its nodes: an indicator adds its own demand to the demand of
  its input, so chains add up. `CrossesAbove` and `CrossesBelow` add one candle for the previous sample.
- **Numbers.** SMA and ATR use the Stock Indicators library in `double` at a decimal boundary (15 significant digits).
  EMA, RSI and the standard deviation are computed in `decimal` only. All are deterministic for identical inputs.

## Primitive indicators

| Key | Inputs and parameters | Formula | First value on closed candle |
|---|---|---|---|
| `SMA` | `in`, `period` (14) | mean of the last `period` samples | `period` |
| `ATR` | candle OHLC, `period` (14) | simple mean of the last `period` true ranges `max(H − L, abs(H − C'), abs(L − C'))`; no Wilder smoothing | `period + 1` |
| `EMA` | `in`, `period` (20) | seed = mean of the first `period` samples; then `EMA = (2·x + (period − 1)·EMA') / (period + 1)`, i.e. alpha = 2 / (period + 1) | `period` |
| `RSI` | `in`, `period` (14) | changes `d = x − x'`; seed G, L = means of the first `period` gains and losses; then Wilder: `A = (A'·(period − 1) + d±) / period`; `RSI = 100 − 100 / (1 + G / L) = 100·G / (G + L)` | `period + 1` |
| `StdDev` | `in`, `period` (20) | population deviation `sqrt(Σ(x − mean)² / period)` over the last `period` samples, two passes | `period` |

Edge cases:

- **EMA** of period 1 is the sample itself. Each step rounds once, to decimal's 28–29 significant digits.
- **RSI** with no losses in its averages is 100, with no gains 0, and with no change at all (a flat window) the
  neutral 50, so a halted instrument signals neither overbought nor oversold.
- **StdDev** of a flat window is exactly 0. The square root is `DecimalSquareRoot`: Newton's iteration in decimal from
  an upper bound, stopped when it stops falling; exact for exact squares and otherwise within a few units in the last of
  28 significant digits. No floating point is involved.

## Multi-output indicators

Bollinger Bands and MACD have several outputs. In the graph they are composites: one node with several output ports,
expanded before validation into primitive nodes. In a block a value expression picks one port with `field`, as for
candle, balance and position fields. Using two outputs means two expressions; a named value shares one.

| Key | Inputs and parameters | Outputs (`field`) | Formula | Warm-up |
|---|---|---|---|---|
| `Bollinger` | `in`, `period` (20), `multiplier` k (`Scalar`, 2) | `upper`, `middle`, `lower` | middle = SMA(period); upper, lower = middle ± k·StdDev(period) | `period` candles for all three |
| `MACD` | `in`, `fastPeriod` (12), `slowPeriod` (26), `signalPeriod` (9) | `macd`, `signal`, `histogram` | macd = EMA(fast) − EMA(slow); signal = EMA(signalPeriod) of macd; histogram = macd − signal | macd on candle `max(fast, slow)`; signal and histogram, and the node, on `slowPeriod + signalPeriod − 1` |

- The middle band is the SMA node itself, so it equals an SMA block with the same input and period.
- A flat window collapses all three bands onto the middle (σ = 0).
- The warm-up of a composite covers all its outputs, also the unused ones: a strategy that reads only the MACD line
  still waits `slowPeriod + signalPeriod − 1` candles.

## Validation

- The block validator reports a period parameter whose default is not an integer from 1 through 200, a MACD whose
  `fastPeriod` is not below `slowPeriod`, and a Bollinger multiplier that is not positive, when those values are
  known before any candle (constants, parameter defaults and arithmetic over them). A missing or unknown `field` is
  reported too. The multiplier must be `Scalar`; an `Integer` constant there is a unit error.
- An optimization range of a period is checked against 1–200 by the warm-up calculator. A range that also produces
  `fastPeriod ≥ slowPeriod` or `k ≤ 0` for some combinations is not filtered generically: those trials compute an
  inverted MACD or inverted bands deterministically. A template that sweeps them states the relation as a template
  parameter rule (`TemplateParameterRules`), as the SMA crossover does for its periods.

## Composing conditions

- Crosses need both sides on the closed-candle clock: MACD line against its signal, a price against a band, a fast
  EMA against a slow one.
- A level is a comparison: `RSI < 30`, `histogram > 0`, `close < lower band` with `Compare`. Crossing a constant level
  is the comparison on this candle together with the opposite comparison on `previous` of the same value.
