# ADR 0072: A Scanner Trade With Its Stop Beyond the Four Hours' Extreme

Status: Proposed, 2026-10-31. The owner asked whether the scanner needs an «затухание импульса» preset for short entries after a
pump; the preset backtest below found that every fade variant loses with the cards' scenarios and pays a little only with a stop
above the pump's peak, so the analyst proposed that scenario first, and the owner chose it ("Да, сценарий сначала"). The buffer and
the window are the analyst's proposal. Builds on ADR 0059 (section 8), ADR 0060, ADR 0061 and ADR 0070; amends ADR 0059 (section 8,
point 3) and ADR 0060 (section 1).

## Context

A short card after a pump puts its stop at the nearest resistance above the price, a quarter of the hours' average range beyond it
(ADR 0070). After a pump the nearest resistance is a swing inside the move, so the next leg of noise takes the stop out while the
fade the short bets on has not started. The peak of the pump is the price that proves the bet wrong.

A preset backtest of fade candidates on Bybit USDT perpetuals (614 pairs with at least 300,000 USDT of turnover on 2026-10-30, the
30 days to 2026-10-30 and 8 days before, ADR 0059 section 6: a signal on entering the result, a one-hour cooldown, entry at the next
minute's open, 0.1 % fee on each side, a turnover floor of 1,000,000 USDT a day) tried about sixty variants built from the existing
metrics (a pump of `Change4h` or `Change24h`, off the high by `DistanceToHigh24h`, `Change15m` not rising, `VolumeSpike15m`) and from
three candidate metrics (the distance below the four hours' high, the age of that high, the last 15 minutes' turnover against the
busiest 15 minutes of the four hours). Results in mean R of the short scenarios, with this ADR's scenario as implemented:

| Preset | Signals | Levels | Target 2R | Pullback | Beyond the 4 h peak | The same, one trade per pair a day |
|---|---|---|---|---|---|---|
| «Импульс» (futures) | 6,527 | −0.05 | −0.05 | −2.71 | −0.04 | −0.06 |
| «Падение» (futures) | 7,161 | −0.04 | −0.08 | −0.10 | −0.02 | −0.04 |
| Every liquid pair every 4 hours | 46,359 | −0.12 | −0.05 | −3.25 | +0.02 | +0.01 |
| Fade: `Change24h` ≥ 15, `DistanceToHigh24h` −20 to −3, `Change15m` ≤ 0 | 3,546 | +0.02 | −0.02 | −0.26 | +0.06 (halves +0.04, +0.08) | +0.16 (halves +0.16, +0.17) |
| Fade with the candidate metrics: a pump of 12 % to the 4 h high, 4–12 % below it, the high an hour old, the turnover at most 0.3 of the busiest | 6,965 | 0.00 | +0.05 | −0.25 | +0.06 (+0.07, +0.04) | +0.08 (+0.10, +0.07) |

- Every fade variant loses with the cards' level scenarios before ADR 0070 (−0.06 to −0.6 R); ADR 0070's buffered stops bring them
  near zero. Only the stop beyond the peak is positive in both halves of the period, and only after a pump: shorting into the move
  («Импульс») loses with it too.
- The edge is thin. The median stop is 6–7 % from the entry, a trade wins 44–49 % of the time and the median trade loses; without
  its five best pairs the fade keeps +0.02 R over every signal and +0.11 R with one trade per pair a day. About sixty variants were
  tried on one month of falling altcoins; funding, the spread and slippage are not modelled.
- The candidate metrics did not beat the existing ones, so no metric is added: a fade preset can be built from today's metrics.
- The long mirror (a stop below the four hours' low) lost on every preset of the month, −0.09 to −0.29 R.

## Decision

1. **Scenario `Extreme`.** Entry at the price (the next minute's open in the backtest), the stop beyond the extreme of the last four
   hours, the target two risks away; the entry waits an hour, as `Levels` and `Target2R` do.
   - **Short:** the extreme is the highest high of the four hours, the stop lies a quarter of the hours' average range above it, and
     the target, two risks below the entry, must be above zero.
   - **Long:** the mirror, the lowest low and a stop a quarter range below it.
   - The stop follows `ScanTradeIdeas.LevelStop` (ADR 0070): no trade when it lies nearer the entry than one average range, the stop
     at the extreme itself when there is no range, eight significant digits rounded away from the entry. The extreme must lie beyond
     the price.
2. **The backtest.** `ScanStudy` takes the lowest low and the highest high of the 240 minutes ending with the signal's minute
   (`ScanTradeIdeas.ExtremeWindow`), the hours' average range at the signal, and runs the trade by the scenarios' rules; a signal
   before 240 minutes have passed has no such trade. The summary lists `Extreme` after `Pullback` in each direction.
3. **The cards.** `ScanTradeIdeasReader` reads the last four hours of the trades' own candles once (the pullback's last hour is
   now the end of the same read) and proposes the scenario once, under the timeframe `4h`, with the average range of the hourly
   levels, as the backtest does. A venue that offers no `1h` or `4h` timeframe has no such card. Shorts come only where shorts are
   allowed (ADR 0061); a preset's trade direction (ADR 0069) picks the side the page opens on.
4. **Trades.** A card of the scenario creates a trade as the other entries at the price do; its bot is named «стоп за пиком 4h» for
   a short and «стоп за дном 4h» for a long. The scenario is stored by name; no migration is needed.
5. **The page.** The backtest's scenario table, the signals' table and the cards name it («Шорт, стоп за максимумом 4 часов, цель
   2R», «За экстремумом 4h»).
6. **No new preset yet.** A built-in «Затухание импульса» waits for the scenario's backtest on 60 to 90 days and for the
   expectancy on the cards (ADR 0071). The owner can save the fade's conditions above as an own preset today.

## Alternatives

- **A built-in fade preset with the existing cards.** Its short cards stop at a resistance inside the pump and lose in every
  variant tried.
- **A fixed percentage above the peak.** The harness's first run used 0.2 %; a quarter of the hours' range keeps the cards' rule
  from ADR 0070 and is wider on the volatile coins that pump.
- **The highest high of 24 hours.** A day's high may be a move that already faded; four hours is the window of the four-hour change
  and the early impulse's range (ADR 0066).
- **New fade metrics.** They did not separate better than the existing metrics in the backtest.

## Consequences

- Futures pairs show one more short card and every pair one more long card, under `4h`; the stops are wide, so a card risks the
  owner's amount with a small position.
- The backtest's summaries gain a row per direction; earlier stored backtests keep their numbers and have no trades of it.
- The long card loses in the month measured; the owner kept it ("Оставить лонг") for symmetry with the other scenarios and for the
  backtest to keep measuring it.
