# ADR 0073: Calibrated Odds of Scanner Cards

Status: Proposed, 2026-11-01. The owner asked for ideas on computing a card's chance more accurately ("можжет есть идеии как более
точно высчитывать вероятность идеи  зачадачи в сканере?"); the analyst proposed four changes, and the owner asked to build all four in one
pull request without further questions ("делай автономно все 4 в одном МРе"). The constants below are the analyst's choice, to be
reviewed in the pull request. Amends ADR 0071, sections 1, 2a, 5, 6 and 7.

## Context

ADR 0071 gives a card the share of the latest preset backtest's cards that reached the target first, among the cards of the same
timeframe, scenario, direction, `1 / (1 + RR)` band (five) and stop band in NATRs (six), and shows no chance below 100 closed trades.
Its analysis found the chance mostly geometry: a gradient-boosted model of 47 features ranked cards with an AUC of 0.66–0.68 against
0.65–0.66 for `1 / (1 + RR)` alone. So a more accurate chance is mainly a better calibrated and steadier one, not a source of edge.
The odds of ADR 0071 have four weaknesses:

1. **Raw shares and a hard cut.** A band of 100 trades shows its share with a standard error of about five points; one of 99 shows
   nothing. Ranking and auto trade minimums take the point value, so a lucky small band passes.
2. **Coarse bands.** One band holds `RR` from 2.33 to 4 and gives all of them one chance; a stopped trade loses the band's mean R,
   though a nearer stop of the same band loses more to the close-based exit (ADR 0071's table: −3.5 R to −1.1 R by stop distance).
3. **One source.** The odds come from the latest backtest of whichever preset, one month of one market. The out-of-sample check of
   ADR 0071 found filters change sign between a rising and a falling month, and pairs found by one preset take odds measured on
   another's signals.
4. **No measure.** Nothing shows whether the chances predict anything out of sample, so no change to them can be judged.

## Decision

### 1. Shrink towards the random walk, level by level

A card's chance starts from its own random walk chance `p0 = 1 / (1 + RR)` and moves by what the backtests measured, at three
levels: the card's kind (timeframe, scenario, direction), its bands, and its bands in the current market band (section 3). At each
level the measured share is shrunk towards what the level above predicts:

`share = (targets + k × prior) / (trades + k)`, with `k = ScanTrades:OddsPriorTrades` (50).

The kind's prior is its trades' mean `1 / (1 + RR)`; a finer level's prior is the level above's shift applied to the finer level's
own mean geometry. A level with no trades takes the level above. Trades and targets count as the independent trades they are worth
(section 2). A kind with fewer than `ScanTrades:MinOddsTrades` (100) closed trades gives no odds; a band needs none of its own.

The cautious chance is the 10th percentile of the last level's share, `share − 1.2816 × √(share (1 − share) / (worth + 1))`, where
`worth` is the level's independent trades plus `k`. The page sorts by the cautious expected R; auto trade rules compare their minimum
expected R and chance with the cautious values (ADR 0071, sections 5 and 6); the proof's stored cards keep both (`chance_low`,
`expected_r_low`), and cards stored before keep their point values.

### 2. Keep the card's geometry, count correlated trades less, model the stop's slip

- **Log-odds shift.** A level keeps `logit(share) − logit(its mean p0)`; the card's chance is `sigmoid(logit(p0) + shift)`, so a
  farther target in the same band has a smaller chance.
- **Independent trades.** Cards of different pairs signalled in the same hour often end alike. The backtest stores per band
  `EffectiveTrades`: the closed trades over the design effect of their signal hours, `Σ_hours (Σ (hit − share))² / (n × share ×
  (1 − share))`, at least 1. Trades of separate hours are worth one each.
- **Slip.** The backtest stores per band the mean distance beyond the stop at which stopped trades left, in hourly NATRs
  (`StopSlip`), and the mean R of trades held to the horizon (`HeldMeanR`). A card's stopped trade loses `−1 − slip / (stop distance
  + stop costs)` in NATRs; the slip is the band's shrunk towards all bands' mean by `k` stopped trades. A missed trade's R mixes that
  with the held trades' R by the band's shares. Odds stored before keep their measured mean R of missed trades.
- Fill shares are the band's shrunk towards the kind's by `k` cards.

### 3. The preset's backtests of separate periods, weighted by age, with the market band

- **Market band.** Bands gain the reference pair's `MarketTrend` (ADR 0071, section 3): above its 50-hour EMA or not. The card takes
  the snapshot's `MarketTrend`; without one the band level is the last.
- **Sources.** The pair's page passes the preset it was found by (`preset`, the preset's name, as backtests store it); auto trades
  pass their rule's preset. Among the owner's latest completed backtests of the venue with cards, those of that preset are used when
  there are any, otherwise all; of one preset, only backtests over periods that do not overlap, newest first, so no signal counts
  twice; at most `ScanTrades:OddsSources` (6). Each weighs `0.5 ^ (age / ScanTrades:OddsHalfLife)` (30 days), the age between its
  period's end and the time the odds are for. The page lists the sources with their weights.
- Odds stored before this decision count their trades as independent, their chance band's middle as their geometry and have no
  market band.

### 4. Calibration on another period and on paper

- **Backtest.** On completion a backtest picks sources as in section 3 for its preset and its last day, leaving out backtests whose
  period overlaps its own, and scores their odds on its closed cards: the share that reached the target against the mean chance,
  the Brier score of the chances, of `1 / (1 + RR)` alone and of ADR 0071's bands (raw shares of bands with 100 trades, otherwise
  `1 / (1 + RR)`), and a table of cards by tenths of the chance. It is stored in the result's summary (`calibration`) and shown as
  «Точность шансов карточек». A backtest with no such sources has none. The backtest's own cards still take the odds of the
  backtest itself for the proof of ADR 0071, section 5.
- **Paper.** A paper trade stores the chance it was taken with (`scan_paper_trades.chance`); the rule's page shows, of the closed
  paper trades with a chance, their mean chance and the share that reached the target.

### Constants

| Setting | Default | Meaning |
|---|---|---|
| `ScanTrades:MinOddsTrades` | 100 | closed trades of the card's kind, across sources, for odds |
| `ScanTrades:OddsPriorTrades` | 50 | imagined trades of the level above at each level |
| `ScanTrades:OddsSources` | 6 | backtests pooled at most |
| `ScanTrades:OddsHalfLife` | 30 days | age at which a backtest weighs half |

The cautious end is the 10th percentile; chances keep at least 0.0001 from 0 and 1. Results are deterministic for identical stored
backtests and inputs.

## Alternatives

- **A trained model in production** (ADR 0071, Alternatives). It ranked a little better than the bands in ADR 0071's analysis and
  did not earn out of sample. The calibration of section 4 is what a model would have to beat; a logistic model with `logit(p0)` as
  an offset and a few features (stop in NATRs, `TrendEma50`, `Rsi1h`, the market band) is the next candidate once a backtest shows
  the levels' Brier score.
- **A beta-binomial interval per band without levels.** Simpler, but an empty band would fall back to the random walk and ignore
  what its kind measured.
- **Matching sources by the preset's conditions** (as ADR 0071's proof does). The pair's page knows only what its link carries, and
  a name is short, stored with every backtest and shown by the page. A rule's preset changes its conditions only by switching the
  rule off.
- **Sorting by the point expected R.** Small bands would lead the list on luck; the cautious value prefers cards whose odds rest on
  more trades.

## Consequences

- Every card of a kind with enough trades gets a chance, also in a band with few trades of its own, and that chance is nearer the
  random walk the fewer independent trades stand behind it.
- Auto trade rules qualify and take cards by cautious values, so a rule needs a little more measured edge than before; existing
  rules keep their stored proof until they are switched again.
- Each backtest of a period apart from earlier ones says whether the chances beat `1 / (1 + RR)`; the owner can run one with
  `daysBack` (ADR 0071, section 2b).
- The JSON of stored odds gains optional fields; odds and outcomes stored before still read. The test venue's invented minutes of
  `BacktestScanSource` swing slightly and have wicks so that its backtest follows filled cards.

## Open questions

- The constants above, especially `k` and the half-life, once calibrations of several periods exist.
- Whether the market band should be the reference pair's 7-day change, which ADR 0071's out-of-sample check named as a hypothesis,
  instead of its 50-hour EMA.
