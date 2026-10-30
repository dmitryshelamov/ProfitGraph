# ADR 0068: Scanner Presets on Every Venue

Status: Proposed, 2026-10-30. The owner asked that every built-in preset be available on every venue ("все эти пресеты должны быть
доступны для всех бирж", "сейчас нету раннего импульса для байбит фьючерса") and approved a fall preset next to each rise, a
breakdown metric for the early fall and a range over trading minutes on T-Invest ("давай четвертый", "да делаем все"). The
thresholds of the new rows are the analyst's proposal. Builds on ADR 0059 (section 4), ADR 0061, ADR 0062 and ADR 0066; amends
ADR 0066 (decision 2, the range).

## Context

The venues offered different built-in presets:

| Preset | Bybit Spot | Bybit USDT perpetuals | T-Invest |
|---|---|---|---|
| «Импульс» | yes | yes | yes |
| «Ранний импульс» | yes | no | no |
| «Падение» | no | yes | no |
| «Под лесенку» | yes | no | yes |

ADR 0066 kept «Ранний импульс» on spot because the preset backtest that chose it ran on spot, and kept it off T-Invest because a
share's four hours before the morning session are the night's flat minutes, so every first move of a session would read as a
breakout. A short mirror of the early impulse needed a distance to the range's low, which no metric gave.

The owner chose to trade with the move: a rise gives long trade cards, a fall short ones. The falls are therefore presets of
their own on every venue, even where shorts are not allowed: there they find a falling pair and alert on it.

## Decision

1. **Five built-in presets on every venue**, in one order: «Импульс», «Ранний импульс», «Падение», «Раннее падение», «Под
   лесенку». Built-in presets stay read-only, with alerts off; the owner copies one to tune it or to alert on it.
2. **Bybit.** Spot and USDT perpetuals have the same conditions; the perpetuals ask for twice the 24-hour turnover (1,000,000 for
   the moves, 2,000,000 for «Под лесенку»), as the perpetuals' «Импульс» already did. «Падение» is an hour down at least 3% on an
   hourly volume spike of at least 3; «Раннее падение» is 15 minutes down at least 2% on a 15-minute spike of at least 3, at or
   below the low of the range before (`Breakdown4h` ≤ 0), an hour at most 6% down, sorted by the 15-minute change ascending.
3. **T-Invest.** No spread condition, as before (ADR 0062). «Ранний импульс» is 15 minutes up at least 1.5% on a 15-minute spike
   of at least 3, at or above the range's high, an hour at most 4% up, 30 million roubles a day; «Падение» an hour down at least
   2%; «Раннее падение» the mirror of the early impulse. The thresholds scale Bybit's by the two thirds that T-Invest's
   «Импульс» does (2% against 3%).
4. **`Breakdown4h`.** (close − the range's lowest low) / that low, %: below zero the price has left the range downwards. It is
   the counterpart of `Breakout4h` everywhere the metrics are: the engine's calculator and `ScanReplay` with the same values, a
   column of the snapshot (migration `202610302100`), the table, filters, sorts, preset and alert conditions.
5. **The range is the minutes with trades** (amends ADR 0066, decision 2). `Breakout4h`, `Breakdown4h` and `Range4h` measure the
   last 240 minutes with trades before the last 15 closed minutes, within the 1,440 minutes the scanner holds; a minute without
   trades is skipped. A morning's range therefore reaches back over the night to the previous session, and a metric is empty
   until 240 such minutes exist. On a liquid Bybit pair nearly every minute has trades and the values are unchanged; on a thin
   pair the range reaches further back.

## Alternatives

- **Skipping only the hours outside a venue's sessions.** Exact for T-Invest, but it puts a session calendar into the engine's
  metric functions; skipping minutes without trades needs no calendar and treats a thin coin the same way.
- **Backtesting every new row first.** The preset backtest on futures and shares is the way to tune them; it does not block
  offering the presets, whose alerts are off until the owner copies one.
- **Falls only where shorts are allowed.** A fall on spot or shares cannot become a short trade, but the owner wants the same
  presets everywhere, and a fall is useful to see before buying.

## Consequences

- The perpetuals' and T-Invest's new rows and both falls on spot reuse thresholds chosen for Bybit Spot's rises; they are a
  starting point, and the preset backtest of the venue says whether a copy needs other ones.
- `Breakout4h` and `Range4h` of a thin pair may differ from the values ADR 0066's backtest saw; the backtest replays the same
  rule, so it stays reproducible.
- Trade cards by the preset's direction (rises long, falls short) are a later change; until then a pair's page shows the cards it
  showed before.
