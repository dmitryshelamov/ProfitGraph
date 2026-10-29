# ADR 0056: Decimal Indicators and Multi-Output Composites

Status: Accepted, 2026-10-29. The owner chose indicators as the next step, before trend and counter-trend templates and
a real grid. Adds EMA, RSI, Bollinger Bands and MACD to the strategy blocks. Supplements the numbers rule of
`docs/architecture.md` and the library SMA and ATR carried over from Profit Fox Designer.

## Context

The block catalog had two indicators, SMA and ATR. Both are the Stock Indicators library (`FacioQuo.Stock.Indicators`)
in `double` behind a decimal boundary that keeps 15 significant digits; the architecture allows `double` for indicator
series. Profit Fox Designer had no EMA, RSI, Bollinger Bands or MACD, so their behaviour is set here from the canonical
formulas. The owner asked for these four in `decimal`, deterministic for identical inputs, without look-ahead and with
the warm-up stated the way SMA (`period` candles) and ATR (`period + 1`) state it.

Two of the four have several outputs: Bollinger Bands three bands, MACD a line, a signal and a histogram. The engine
evaluates a primitive node to one value written to all of its output ports, while composites (`Graphs/Composites`)
already give one node several output ports by expanding it into primitive nodes before validation. Blocks pick one
output of a multi-output source with `field`, as they do for candle, balance and position fields.

## Decision

1. **Decimal indicators.** EMA, RSI and the population standard deviation are new primitive indicator nodes (`EMA`,
   `RSI`, `StdDev`), computed in `decimal` by run-local accumulators in `Engine/Evaluation`, without the library and
   without floating point. SMA and ATR stay as they are, so no existing result changes.
2. **Formulas.**
   - EMA: seed = simple mean of the first `period` samples, then `EMA = (2·x + (period − 1)·EMA') / (period + 1)`
     (alpha = 2 / (period + 1), one rounding per sample).
   - RSI: Wilder. Seed averages of gains and losses are simple means of the first `period` changes; then
     `A = (A'·(period − 1) + change) / period`. `RSI = 100·G / (G + L)`, algebraically `100 − 100 / (1 + RS)`:
     100 without losses, 0 without gains, and 50 when nothing changed. Implementations disagree on the flat case
     (100, 0 or undefined); 50 is chosen so a halted instrument reads neither overbought nor oversold.
   - Standard deviation: population (divide by `period`, as John Bollinger defines the bands), two passes over the
     window, so a flat window is exactly 0. The root is `DecimalSquareRoot`: Newton's iteration in decimal from an
     upper bound found by scaling into [1, 100), stopped at the first iterate that does not fall; exact squares give
     exact roots, other values are within a few units in the last of 28 significant digits.
3. **Multi-output indicators are composites.** `Bollinger` = SMA(period) as the middle band and middle ± k·StdDev(period)
   as the outer bands, with ports `upper`, `middle`, `lower`. `MACD` = EMA(fast) − EMA(slow), its EMA(signal) and their
   difference, with ports `macd`, `signal`, `histogram`. The middle band reuses the SMA node, so it equals an SMA block
   with the same input and period. No engine change is needed for several values per node; a block expression of kind
   `Bollinger` or `MACD` names its port in `field`, and the lowerer wires that port.
4. **Warm-up.** A primitive indicator states its demand through `WarmupPeriodKey` and `WarmupOffset`: EMA and StdDev
   `period` candles (offset −1, as SMA), RSI `period + 1` (offset 0, as ATR). A composite needs nothing of its own:
   demand adds up along its inner chain, so Bollinger needs `period` candles and MACD `slowPeriod + signalPeriod − 1`
   for its signal and histogram. The warm-up of a composite covers all of its outputs, also those a strategy does not
   read. A node is unavailable until warmed up, and an unavailable sample restarts it, which matches SMA's rule that a
   window with a missing sample has no value.
5. **Parameters.** Periods keep the `period` parameter on the primitive nodes, so the 1–200 static bound, Integer
   typing of wired periods and optimization ranges work unchanged; the composites map `fastPeriod`, `slowPeriod`,
   `signalPeriod` and `period` onto them, and the multiplier onto a `Scalar` constant. The block validator reports
   `fastPeriod ≥ slowPeriod` and a multiplier that is not positive when both are known before any candle. Combinations
   inside optimization ranges are not filtered generically; a template that sweeps them adds a template parameter rule.

## Consequences

- EMA, RSI and StdDev results do not depend on the processor or on a library's `double` rounding; Bollinger's middle
  band keeps SMA's 15 significant digits, its width is decimal.
- Indicators that publish several values do not need an engine change: new ones follow the composite pattern.
- A strategy that reads one MACD output waits for the whole MACD. Reading the line earlier would need per-port warm-up,
  which the engine does not have.
- `CrossesAbove`/`CrossesBelow` need both sides on the candle clock, so "RSI crosses 30" is a comparison with the
  previous value, not a cross with a constant. Templates built on these indicators state their parameter relations as
  template rules.
- `docs/node-catalog.md` lists every indicator with its formula, warm-up and edge cases.
