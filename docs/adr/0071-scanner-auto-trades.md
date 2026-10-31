# ADR 0071: Scanner Auto Trades by the Odds of a Card

Status: Proposed, 2026-10-30. The owner asked how to create bots from the scanner's best ideas automatically ("как нам
автоматизировать, чтобы мы автоматически создавали ботов с лучшими идеями"), asked for this decision ("Да, пиши ADR"), for an
analysis of how to pick the ideas beyond reward-to-risk, trend and indicators included ("проведи аналитику", "смотри прод, даю
апрув"), and of which ideas are most likely to work out ("какие идеи вероятнее всего исполнятся, как это определять"). The numbers
below come from that analysis; every rule is the analyst's proposal, to be reviewed in the pull request. Builds on ADR 0011,
ADR 0042, ADR 0059 (sections 5, 6 and 8), ADR 0060, ADR 0061, ADR 0065, ADR 0067, ADR 0069 and ADR 0070; it is the first version of
the screener robot (roadmap, market scanner step 5).

Amended 2026-11-01 by [ADR 0073](0073-scanner-card-odds-calibration.md): a card's chance is shrunk towards `1 / (1 + RR)` level by
level and has a cautious end that sorts cards and proves and admits rules; odds pool the preset's backtests; backtests score them.

Amended 2026-10-31: the owner asked to check the analysis on another period and on futures ("Да, проверь") and to add the result
here ("Да, в PR #240"). The check is "Out-of-sample check" below; it adds the second period to section 5.

## Context

The scanner finds pairs; the owner opens a pair, reads its trade cards and ticks the wanted ones, and «Создать выбранные» starts a
one-trade bot for each (ADR 0060). Everything after the click is automatic. What a human still does is decide **which card, and
whether to trade at all**, and the page helps with one number: the card's reward-to-risk.

### What production showed (read on 2026-10-30 with the owner's permission)

- The only stored preset backtest, «Импульс (копия)» on Bybit Spot (2026-10-08 to 2026-10-28, 388 pairs, 2,000 newest of 2,826
  signals, stored before the trade scenarios existed), shows the price after a signal falling on average: −0.22 % after 4 hours,
  −1.98 % after 24 hours. It splits by how far the pair had already gone, the same way in both halves of the period:
  - not extended (7-day rise under 26 %, 24-hour change under 9 %, NATR under 3, volume spike under 6; 403 signals): +0.61 % after
    4 hours and +0.70 % after 24 hours;
  - extended (7-day rise over 50 % or 24-hour change over 20 %; 636 signals): −0.98 % and −4.79 %.
- Of the 19 finished card trades, 6 reached the target and 13 the stop, −4.65 USDT together (mean −0.44 R). Stops under 1 % of the
  entry lost −0.86 R on average; the close-based stop sold GROVEUSDT at −5.1 R and TRXUSDT at −2.6 R.

### What the cards would have done (public Bybit Spot minutes, replayed off-line)

Every card of `ScanTradeIdeas.Of` on the levels of `1m`…`1d` was replayed at 14,705 signals of three loose presets (an hour up 2 %
on a volume spike of 2, 15 minutes up 1 % on a 15-minute spike of 2, an hour down 2 % on a spike of 2; turnover over 500,000) on the
120 USDT pairs with the largest turnover today, over 2026-09-30 to 2026-10-30: about 285,000 closed cards, long and short. Each was
followed by the bot's rules, not the backtest's: a limit entry filled when a minute traded through it within the card's wait, the
target when a minute traded through it, the stop on a minute's close beyond it with a market exit at that close; fees 0.1 % limit
and 0.1 % market, 0.05 % spread on a stop, at most 72 hours in a trade.

1. **A card's chance is mostly its geometry.** A target `RR` risks away is reached first with a chance close to `1 / (1 + RR)`, the
   chance of a random walk: 0.52 against 0.50 for «Уровни» on `1h`, 0.37 against 0.33 for «Цель 2R». A higher reward-to-risk is a
   lower chance, not a better card. The market-price entries fill almost always; «Откат» fills 72–78 % of the time.
2. **Costs and the close-based stop decide the result.** By the stop's distance in hourly NATR:

   | Stop distance | Cards | Target first | 1 / (1 + RR) | R of a stopped trade | Mean R |
   |---|---|---|---|---|---|
   | ≤ 0.25 NATR | 106,000 | 0.41 | 0.35 | −3.54 | −1.67 |
   | 0.25–0.5 | 69,000 | 0.45 | 0.42 | −1.68 | −0.43 |
   | 0.5–1 | 53,000 | 0.46 | 0.44 | −1.39 | −0.24 |
   | 1–1.5 | 20,000 | 0.48 | 0.46 | −1.24 | −0.12 |
   | 1.5–3 | 22,000 | 0.47 | 0.49 | −1.16 | −0.11 |
   | > 3 | 14,000 | 0.40 | 0.53 | −1.07 | −0.06 |

   A tight stop is touched by noise and then exited at a minute's close well beyond it: the stopped trade loses several risks, not
   one. ADR 0070 already drops stops within one average range; beyond that the cards lose about the round trip's costs.
3. **Trend and indicators move the chance by a few points, against the move.** With stops of at least 1 NATR, the mean R in each
   third of the month:

   | Filter (in the card's direction) | Cards | Mean R | Thirds |
   |---|---|---|---|
   | none | 56,446 | −0.104 | −0.102, −0.092, −0.118 |
   | price beyond the 50-hour EMA against the card (a dip for a long) | 25,480 | −0.014 | −0.027, +0.004, −0.022 |
   | hourly RSI below 45 for a long, above 55 for a short | 20,615 | −0.011 | −0.024, +0.010, −0.021 |
   | BTC below its 50-hour EMA for a long, above for a short | 26,267 | −0.003 | −0.031, +0.035, −0.019 |
   | hourly RSI above 65 for a long, below 35 for a short | 14,594 | −0.211 | −0.135, −0.278, −0.215 |
   | BTC with the card | 30,179 | −0.193 | −0.159, −0.207, −0.207 |
   | 24-hour move over 15 % already in the card's direction | 4,697 | −0.180 | −0.159, −0.073, −0.307 |

   Buying into strength right after an impulse is the worst choice; the best filters only bring the cards to break-even.
4. **A model ranks the cards but does not make them pay.** A gradient-boosted model of 47 features (the geometry, the stop and target
   in ranges and NATR, touches of the levels, the scanner's metrics, the 20/50/200-hour EMAs and their slope, hourly RSI, BTC's
   change and trend, the hour) was trained on one half of the month and tested on the other. It is well calibrated (deciles of
   0.14…0.82 predicted against 0.17…0.80 observed) and beats `1 / (1 + RR)` a little (AUC 0.66–0.68 against 0.65–0.66). The card of
   highest expected R per signal made +0.01 to +0.07 R in one direction of the test and −0.07 to −0.15 R in the other.

Limits of the analysis: one month and one market regime; spot prices stand in for futures shorts; the universe is today's liquid
pairs (survivors); cards of one signal overlap; the stored production backtest predates the scenarios.

### Out-of-sample check (2026-10-31)

The best rule a narrower search had found on the same month was replayed on three more sets: Bybit Spot over 2026-08-31 to
2026-09-30 (the same 120 pairs), and Bybit USDT perpetuals over both months (the 120 perpetuals with the largest turnover on
2026-10-30). The rule: the rise preset, a long «Цель 2R» on hourly levels, a stop at least 1 % from the entry, the day down
(`Change24h < 0`) and the price more than 10 % below the 7-day high (`DistanceToHigh7d < −10`). Trades were followed by the bot's
rules as above (24 hours at most), with two engines: the code before ADR 0070 (stop at the level) and `main` with ADR 0070 (a stop a
quarter range beyond the level, none within one range).

The two months were opposite markets: in September BTC rose 7 % and the median pair 25 % (88 % of the pairs rose); in October BTC
fell 7 % and the median pair 19 % (15 % rose).

| Mean R, bot's rules | Spot Oct (searched) | Spot Sep | Perpetuals Oct | Perpetuals Sep |
|---|---|---|---|---|
| The rule, stop at the level | +0.23 (385) | −0.24 (199) | +0.10 (665) | −0.27 (325) |
| The rule, ADR 0070 stops | +0.33 (267) | −0.03 (120) | +0.19 (416) | −0.13 (168) |
| Target 2R, ADR 0070 stops, stop ≥ 1 %, no other filter | −0.02 (970) | +0.08 (1,527) | +0.08 (1,311) | +0.12 (1,691) |
| The same above a rising 50-hour EMA (with the trend) | −0.14 (559) | +0.09 (1,287) | +0.03 (742) | +0.16 (1,355) |
| The same below the 50-hour EMA (a dip) | +0.32 (284) | +0.05 (139) | +0.18 (450) | −0.17 (188) |

1. **The rule does not hold.** It earned in October on both markets, which moved together, and lost in September on both. It
   buys dips, and dips paid only in a falling market.
2. **Trend filters change sign with the market.** Point 3 above (against the move is least bad) is October's; in September buying
   with the trend was the better half. A filter chosen on one month of one regime is not evidence.
3. **ADR 0070's stops are the one robust gain.** Without them «Цель 2R» after a rise with a stop of at least 1 % made −0.04 to
   +0.01 R; with them +0.08 to +0.12 R in three sets of four, though not in each third, so it does not pass section 5.
4. **A hypothesis, not a finding:** taking the trend when BTC rose over the last 7 days and the dip when it fell made +0.09 to
   +0.16 R in all four sets. It was formed after seeing September, so it needs a period neither month covers (or paper mode).

So the best idea is not the card with the highest reward-to-risk, and today no card rule earns after costs. Automation must be
**able to wait** for a rule that does, and must judge cards by their odds and costs.

## Decision

### 1. A card shows its odds and its expected result

Next to its reward-to-risk a card shows:

- **Chance of the target first:** the share of cards like it that reached their target before their stop in the venue's latest preset
  backtest, where "like it" is the same scenario, direction and timeframe and the same band of `1 / (1 + RR)` (five bands) and of
  the stop's distance in NATR (the bands of the table above). A band with fewer than 100 backtest cards shows no chance.
- **Expected R after costs:** `chance × (reward − costs at the target) − (1 − chance) × (the band's mean R of a stopped trade)`,
  with the costs of ADR 0065. A stopped trade's R is the band's measured one, not −1, because the stop exits on a close.

Ideas are sorted by expected R, not by reward-to-risk. The page labels the chance as measured on history, not as a forecast.

### 2. The preset backtest measures cards the way the bot trades them

`ScanStudy` follows each card by the bot's rules: the limit entry and the target fill when a minute trades through them, the stop
acts on a minute's close beyond it and exits at that close, the fees are the limit and market rates of ADR 0065 with the spread of
the snapshot's median when the venue has no quote history. It stores per card the bands of section 1, so the page's chances come
from the backtest. Earlier stored backtests keep their numbers.

### 2a. How sections 1 and 2 are built (amended 2026-10-31)

The owner asked to build the whole decision at once ("делай все сразу"); sections 1 and 2 came first, with these choices:

- **The same cards.** `ScanTradeIdeas.Compose` (Engine) composes a pair's cards from the levels of each timeframe, the last hour and the
  last four hours; the pair's page and the backtest both call it. At a signal the backtest takes the timeframes of
  `ScanTrades:LevelTimeframes` the venue offers, their closed candles aggregated from the minutes on the timeframe's grid
  (`ScanCandleFrames`), and the signal minute's close as the price. Early in the period a timeframe has fewer than the page's 168
  candles (the history starts eight days before the period), so its levels may differ from the page's.
- **The bot's rules** (`ScanCardFollow`). The entry rests at the card's price for the card's wait and fills at that price when a minute
  trades through it; the target rests from the next minute and fills at its price when a minute trades through it, also in a minute
  that closes beyond the stop; the stop leaves at a close beyond it. A trade still open after 72 hours leaves at that close by market,
  since the analysis held trades at most that long. A result is in R of the card's sizing (ADR 0065): a stop exactly at its price is
  −1 R.
- **Costs.** The owner's fee rates of the venue read once per backtest with the universe's first pair, as a card reads them, and
  `ScanTrades:FeeRate` without them; the spread is the pair's in the current snapshot, or the snapshot's median spread. Cards whose
  stop is nearer than those costs are left out, as on the page.
- **Odds.** Per timeframe, scenario, direction and the two bands the backtest stores the cards proposed, filled and closed, those that
  reached the target and the stop, and the mean R of all and of those that missed the target (`ScanCardOdds`, column
  `scan_backtests.card_odds`). The page reads the owner's latest completed backtest of the venue that has them, of whichever preset,
  and names it. The expected result is `fill × (chance × the card's R at its target + (1 − chance) × the missed mean R)`: unfilled
  entries make nothing, and the missed mean R counts trades held to the horizon with the stopped ones, so it is the measured loss of a
  card that did not reach its target rather than −1. A band needs `ScanTrades:MinOddsTrades` (100) closed trades.

### 2b. A backtest of an earlier period (amended 2026-10-31)

Section 5 needs a second backtest over another 30 days. `POST /api/scanner/{venue}/backtests` takes `daysBack` (0 to 335): the period
ends that many days before now. The scanner page offers the last 30 days and the three 30-day periods before them.

### 3. Trend, RSI and the market as scanner metrics

Three metrics join the table, presets, alerts and replay, computed by the engine from the hours the scanner holds:

- `TrendEma50`: (price − EMA of the last 50 hourly closes) / that EMA, %; and `EmaSpread20x50`: (EMA20 − EMA50) / EMA50, %.
- `Rsi1h`: Wilder's RSI of 14 hourly closes (the engine's indicator, ADR 0056).
- `MarketTrend`: the venue's reference pair (Bybit: `BTCUSDT`) as `TrendEma50`, the same value on every row.

Each average is seeded by the simple mean of its first closes among the up to 168 hours the scanner holds, so the live snapshot and
the replay compute the same value; the reference pair is the venue scan source's (`IMarketScanSource.ReferenceSymbol`), and the
preset backtest replays it first and gives its trend to every pair's minutes (amended 2026-10-31).

They let a preset say "a dip in a market that is not rising", which point 3 of the context found to be the least bad. They are
filters for presets and auto trade rules to test, not advice.

### 4. An auto trade rule on an own preset

An owner may add **auto trades** («Автосделки») to a preset of their own; built-in presets are copied first, as for alerts. The rule
(`ScanAutoTradeRule`, Domain, table `scan_auto_trade_rules`, one per preset):

| Field | Meaning | Default |
|---|---|---|
| Direction, scenario, timeframe | which card of a signal to take; short only on a venue with shorts (ADR 0061), direction as the preset gives it (ADR 0069) | chosen from the backtest |
| Minimum expected R | the card's expected R of section 1 | 0.1 |
| Minimum chance | the card's chance of section 1 | none |
| Mode | `Off`, `Paper` or `Live` | `Off` |
| Open trades at most | of this rule at once | 3 |
| Trades per UTC day at most | | 10 |
| Daily loss limit | quote lost by this rule's closed trades in the UTC day; reaching it pauses the rule until the next day | 3 × the settings' risk |

Sizing, the credential, the leverage and the strategy come from the owner's `scan_trade_settings`, as for a card. A pair has at most
one live trade of a rule; trades the owner creates by hand do not count against a rule's limits.

### 5. A rule trades only what its preset's backtest proved

A rule switches to `Paper` or `Live` only with a completed preset backtest of the venue whose conditions equal the preset's, in which
the rule's cards (its scenario, direction, timeframe and minimums) had:

- at least `ScanAutoTrades:MinBacktestTrades` (100) closed trades, and
- a mean R after costs (section 2) of at least `ScanAutoTrades:MinBacktestMeanR` (0.05 R) **in each third of the period**, so that
  one good week does not carry it, and
- the same in a second completed backtest of another 30 days in which the venue's reference pair (Bybit: `BTCUSDT`) moved the
  other way, so that one market regime does not carry it (amended 2026-10-31, "Out-of-sample check").

The rule keeps the backtest's id and its numbers, and the page shows them beside it. Changing the preset's conditions switches the
rule `Off`. The page lists the scenarios and timeframes of the latest backtest with their trades and mean R per third and marks those
that qualify; by the analysis above none qualifies today, and the page says so instead of offering a switch.

### 6. Signal, request and handling

1. **Signal.** A pair that enters the preset's result, at most once per pair and rule per hour: the alerts' rule and the
   backtest's. The rule keeps its remembered pairs, checked by `ReplaceScanSnapshot` in the snapshot's transaction next to the alert
   watches; the first check after a switch only records the result.
2. **Request.** An entering pair stores a `scan_auto_trade_requests` row (rule, pair, snapshot time, price). The `scanner` role does
   nothing else: it reads no fees, starts no bots and calls no venue (ADR 0059, section 2).
3. **Handling.** `ScanAutoTradeHost` in the Worker's `live` role claims requests (`FOR UPDATE SKIP LOCKED`) and, for each:
   1. skips it as `expired` when its snapshot is older than `ScanAutoTrades:RequestLifetime` (2 minutes);
   2. skips it as `paused`, `limit_open`, `limit_day` or `pair_busy` when the rule is not on or a limit holds;
   3. reads the pair's cards with `ScanTradeIdeasReader`, as the page does, takes the rule's card and skips the request as
      `no_idea` when there is none or it is below the rule's minimums;
   4. in `Live`, sends `CreateScanTradeCommand` with the card's prices: every guard of ADR 0060, 0065, 0067 and 0070 applies, and a
      refusal is stored with its error code;
   5. in `Paper`, stores a paper trade (section 7).

   A request ends as `created`, `paper` or a skip reason; requests are kept 30 days, so the page shows why a signal did not trade.

### 7. Paper mode

Real orders are switched per Worker (ADR 0017), so paper mode cannot be a bot. A paper trade (`scan_paper_trades`) is the card's
entry, stop, target, amount and costs, followed in the `scanner` role on the closed minutes it holds by the rules of section 2. A
rule switches to `Live` only from `Paper`, after at least `ScanAutoTrades:MinPaperTrades` (30) closed paper trades whose mean R is
not below the backtest's by more than half; the page shows both side by side.

### 7a. How sections 4–7 are built (amended 2026-10-31)

- **Proof.** A backtest stores each closed card trade with its third of the period and the chance and expected R its kind and bands
  give it in that backtest (`scan_backtest_cards`), and the move of the reference pair over its period (`market_change`). A rule's
  cards in a backtest are those of its kind whose expected R and chance reach its minimums. The proof is the owner's latest completed
  backtest whose conditions equal the preset's (in any order) and the latest one that ended before it began with the reference pair
  moving the other way; the rule keeps both ids and their numbers per third. A missing or failing proof answers 422
  `scan_auto_trade_not_proven`.
- **Handling in three steps.** Reading a pair's cards reads the venue, which may not happen inside a transaction, so the Worker claims a
  request in one transaction (it becomes `Handling` after the gate of section 6 passes), reads the cards as the page does, and takes
  the card in a second transaction after checking the gate again. A request left claimed for longer than the request lifetime is taken
  up again and expires.
- **Paper steps.** After each snapshot the scanner steps the venue's open paper trades through the last 60 closed minutes of their
  pairs, each minute once, by `ScanCardFollow.Next`, the same step the backtest follows cards with. A paper trade's result is in R of
  its sizing and in quote (R times the risk). The rule's paper result counts its paper trades since it entered its mode.
- **Limits.** Open trades are those waiting for their entry or open; the day's loss is the net loss of the trades closed that UTC day.
  A rule at its loss limit pauses when a trade closes or a request is handled.
- **Not here yet.** Section 8: the mode request accepts `Off` and `Paper`; `Live` answers 409 `scan_auto_trade_live_unavailable`.

### 8. Live mode and safety

- `Live` is set only by the owner on the page, with a confirmation naming the limits and the risk. Nothing in the committed
  configuration changes; without `Live:Trading:Enabled` the rule's bots trade simulated, as every bot does.
- A rule pauses at its daily loss limit and resumes at the next UTC midnight. «Остановить все автосделки» switches every rule `Off`;
  created trades keep running as ordinary bots with their stops and kill switch.
- Each created trade and each pause sends one Telegram message through the queue of ADR 0042; fills and closes reach Telegram as for
  every bot.
- A trade created by a rule records it (`scan_trades.auto_rule_id`); the tracker of ADR 0060 follows it unchanged, and its realized
  profit feeds the rule's daily loss.

### 9. Domain

`ScanAutoTradeRule` changes state only through its methods: `SwitchToPaper(backtest)` and `SwitchToLive(paperResult)` check
sections 5 and 7 and the order `Off → Paper → Live`; `SwitchOff()`; `Pause(day)` and `Resume(day)`; `ConditionsChanged()` switches
it off; `Admits(openTrades, todayTrades, todayLoss)` checks the limits. Each transition has a Domain unit test.

### 10. Order of work

1. This decision.
2. Section 2: the backtest follows cards by the bot's rules and stores their bands; section 1: chance and expected R on the cards.
3. Section 3: the trend, RSI and market metrics.
4. Sections 4–7 in `Paper` mode.
5. Section 8, only once a rule has qualified in the backtest and on paper.

## Alternatives

- **Rank by reward-to-risk** (today's page). The analysis shows a higher reward-to-risk is a proportionally lower chance; ranking by
  it favours targets rarely reached.
- **A trained model in production.** It ranks a little better than the bands but did not earn out of sample, and it would be a model
  to retrain, version and explain. The bands of section 1 are counts the backtest can reproduce; a model can replace them once it
  beats them out of sample in every third.
- **Trade with the trend.** Filters that buy into strength right after an impulse were the worst in the analysis, and BTC rising
  with a long did not help it.
- **Stops on the exchange.** A stop order resting on the venue would exit near its price instead of at a minute's close beyond it,
  which section 2's table puts at 0.1–2.5 R per stopped trade. It is a second order path (ADR 0060, Alternatives) and stays a
  separate decision; this one measures the stop the bot has.
- **Create bots in the `scanner` role**, **Telegram cards with «Создать»**, **paper as a bot on a simulated venue**, **running a saved
  strategy on every found pair**: as in the first draft of this decision; the first two are cheaper, the last two larger, and none
  needs a rule that earns less.

## Consequences

- Cards say how often cards like them worked and what they are expected to make after costs; a high reward-to-risk no longer looks
  like a good card by itself.
- The preset backtest's results change for every scenario, since stops exit on closes; old backtests keep their numbers.
- No rule qualifies today, so the first visible result is the cards' honest odds and the backtest's numbers; automation switches on
  only when a preset and scenario earn in every third of a backtest and on paper.
- The analysis points at where an edge might be, for later decisions: the extended pairs after an impulse fell 4.8 % within a day on
  production signals, but a short with a stop within the next hour's noise did not capture it; a trade held for hours with a wider
  stop or a time exit would need its own scenario.

## Open questions

- The thresholds of section 5 (100 trades, 0.05 R in each third) and the defaults of section 4.
- The bands of section 1, and whether to add the trend filters of section 3 as a band once they are metrics.
- Whether a stop order on the venue (Alternatives) should come before the automation, since the close-based stop costs more than any
  filter gains.
