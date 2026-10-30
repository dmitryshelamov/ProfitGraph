# ADR 0066: Early Impulse Metrics in the Scanner

Status: Proposed, 2026-10-30. The owner asked how to find impulses early in the scanner and then to add the metrics proposed for
it ("Да, добавить"): a 5- and 15-minute volume spike, a breakout of the range before the move and the range's width, with a
built-in preset checked by the preset backtest. The thresholds are the analyst's proposal from that backtest. Builds on ADR 0059
and ADR 0062; amends ADR 0059 (section 3, metrics; section 4, presets).

## Context

«Импульс» (ADR 0059, section 4) keeps a pair whose last hour rose at least 3% on a volume spike of at least 3. Both conditions
lag a move that starts with one or two large minutes:

- **The volume spike is hourly.** A burst of five minutes is diluted by the other 55; the spike reaches 3 only after a large
  part of the move.
- **3% over an hour is a move already made.** A pair leaving a quiet range passes it on the first large candles, often well
  above the range.
- **No metric sees the range.** The distances to the highs of 24 hours and 7 days include the move itself, so they cannot say
  that the price has just left a narrow range, and the volatility and efficiency ratio over 24 hours mix the quiet hours with
  the move.

The preset backtest of «Импульс» over 30 days lost on every scenario after fees (roadmap, step 5a): the signal comes after much
of the move. The scanner sees closed minutes only, so a minute is the shortest delay; order flow and the order book stay out of
scope (ADR 0059, section 3).

## Decision

1. **Short volume spikes.** `VolumeSpike5m` and `VolumeSpike15m`: the turnover of the last 5 and 15 closed minutes over the
   median hourly turnover of the volume spike's baseline (the hours with trades among the 167 closed before those minutes
   start, at least 72, ADR 0062), scaled to the window by a twelfth and a quarter. The baseline is the hourly one, not a median
   of 5- or 15-minute windows: the live scanner holds 1,440 minutes, a week of hours is the only longer history it has, and the
   same median keeps the three spikes comparable. A quarter of the median hour is above the median 15-minute window, so the
   short spikes read lower than a median of windows would; the thresholds account for it.
2. **The range before the move.** The four hours of minutes before the last 15 closed minutes are the range; the last 15 are
   the move being measured, so a move does not widen its own range.
   - `Breakout4h`: (close − the range's highest high) / that high, %. Above zero the price has left the range upwards.
   - `Range4h`: (highest high − lowest low) / lowest low, %. A narrow range is the compression a breakout starts from.
   Both are empty until 255 minutes exist.
3. **Everywhere the other metrics are.** The four metrics are pure functions in the engine's `ScanMetricCalculator`, computed in
   constant time per minute by `ScanReplay` with the same values; they are columns of the snapshot (migration
   `202610300800`), the table, filters and sorts, conditions of presets and alerts, and the pair's page shows the 15-minute spike
   and the breakout among its headline metrics.
4. **«Ранний импульс».** A built-in preset of Bybit Spot: `Change15m` ≥ 2, `VolumeSpike15m` ≥ 3, `Breakout4h` ≥ 0, `Change1h`
   ≤ 6 (the move has not run away yet), `Turnover24h` ≥ 500,000 and `Spread` ≤ 0.1 (an early entry is a market order, which
   crosses the spread; history has no quotes, so the backtest does not check it), sorted by the 15-minute change. The thresholds
   come from the backtest below. USDT perpetuals and T-Invest get no such preset yet: the backtest ran on spot only, and a share's
   four hours before the morning session are the night's flat minutes, so every first move of a session would read as a
   breakout. The owner copies the preset to tune it or to switch its alerts on.

## Backtest

Bybit Spot, 388 pairs, 30 days to 2026-10-30, the preset backtest's rules (ADR 0059, section 6: a signal on entering the
result, a one-hour cooldown, entry at the next minute's open, R after 0.1% fees on each side), computed by `ScanReplay` and
`ScanStudy` over Bybit's public minutes. The same run gives «Импульс» 2,829 signals and −0.30 R for «уровни», as its live
backtest did (#218: 2,825 and −0.29 R).

| | «Импульс» | «Ранний импульс» |
|---|---|---|
| Signals (pairs) | 2,829 (234) | 1,985 (225) |
| Change after 1 h, mean / median | −0.20% / −0.43% | −0.17% / −0.49% |
| Change after 24 h, mean / median | −2.03% / −3.02% | −1.08% / −2.41% |
| Largest rise within 24 h, median | 5.67% | 4.63% |
| «Уровни», mean R (winning) | −0.30 (46%) | −0.29 (41%) |
| «Уровни R:R ≥ 2», mean R | −0.57 | −0.32 |
| «Цель 2R», mean R | −0.31 | −0.39 |
| «Откат», mean R | −0.12 | −0.18 |

- **Earlier.** 46% of «Импульс» signals had a «Ранний импульс» signal of the same pair within the two hours before, a median 11
  minutes earlier at a price a median 0.60% lower (mean 0.97%).
- **Not profitable.** Like «Импульс», no scenario pays after fees. A grid of about 130 variants (15-minute change 1–3%, 15-minute
  spike 3–8, breakout 0–0.5%, hour at most 4–6%, range at most 2–5% or none) found none that pays in both halves of the
  period; the variants that did on a 78-pair sample or in one half lost in the other. A narrow range (`Range4h`) did not help
  reliably and stays a metric, not a condition.
- The preset is a way to see a move sooner, not a trading rule; a rule needs a better exit than these scenarios (roadmap,
  step 5, the screener robot).

## Consequences

- Early signals come before the volume of a move has confirmed it, so more of them fade; the preset backtest, not the live
  table, says whether a variant pays after fees, and the owner tunes a copy of the preset the same way.
- The 15-minute spike makes «Импульс» itself cheaper to tighten: a copy can ask for both an hourly and a 15-minute spike.
- A short mirror (a breakdown below the range) needs a distance to the range's low; it is added when a short preset needs it.
