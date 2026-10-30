# ADR 0067: A Scanner Trade Enters Only Between Its Stop and Target

Status: Accepted, 2026-10-30. The owner asked for it after a review of stopped production trades ("Да, начинай" to a fresh-price
check before the entry is sent). Proven on the test venue with the scripted gateway; no real venue was contacted by the tests.
Builds on ADR 0060 (section 4), ADR 0061 and ADR 0018.

## Context

A scanner trade (ADR 0060) is a bot whose strategy places a limit entry at the card's price on start. On 2026-10-30 eight of
eleven production trades ended at their stops, and two of them were lost before they began:

- **MAGMAUSDT, three shorts.** The cards' entry was 0.3082 with stops above it; the shorts sold at 0.3371–0.3383, already above
  every stop, and the next minute's close sold them back (−0.768 USDT together). A limit sell below the market fills at once at
  the bid, so the limit price did not protect the trade: for a short a higher fill is a better entry, but the market had already
  crossed the stop.
- **GROVEUSDT.** The entry bought while the last close was below the stop, so the target was not placed and the stop was held
  back by a position check (fixed in #233); the sale came 5 h 34 min later (−1.807 USDT).

The creation guard compares the card's entry with the scanner snapshot's price within 5 %. Nothing looks at the price again
when the entry is sent, which happens after the bot starts, warms up and the Worker's dispatcher claims the intent.

## Decision

1. **The rule.** A trade's entry may go only while the price lies strictly between its stop and its target: `stop < price <
   target` for a long, `target < price < stop` for a short (`ScanTrade.AllowsEntryAt`, Domain). A limit entry then fills at its
   price or better, never beyond the stop; past the target the trade has nothing left to take. A fill nearer the stop than the
   card's entry only lowers the risk the amount was sized for, so the amount is not recomputed and the entry stays a limit.
2. **Before the entry is sent.** The dispatcher asks every `IOrderSubmitGuard` (Application, Bots port) after the per-order limit
   and the sell price floor (ADR 0018); a refusal rejects the submit with `RiskLimit` before it reaches the venue, so the bot's
   alerts and Telegram show it. `ScanTradeEntryGuard` (Application, Scanner) answers for the `level.entry` order of a waiting
   trade's bot and passes every other order and bot.
3. **Its price.** The venue's last trade of the pair (the bot's display price, S5-19) when it is newer than the bot's last closed
   candle, else that candle's close. A price older than `ScanTrades:EntryPriceMaxAge` (3 minutes) is not trusted, and the entry
   is refused too. The check is live-only: the engine and backtest replay do not see it.
4. **The refused trade.** The guard fails the trade with `scan_trade_entry_beyond_levels` (price beyond the stop or the target)
   or `scan_trade_entry_price_unknown` (no recent price). The tracker then stops its bot as for any finished trade. The entry is
   placed only on start, so a refused trade never buys; the owner creates a new one from fresh cards.
5. **When the trade is created.** The creation guard takes the venue's last trade when a bot already follows the pair and it is
   newer than the snapshot, else the snapshot's price; besides the 5 % deviation it refuses a price beyond the stop or the target
   with 409 `scan_trade_entry_beyond_levels`.

## Alternatives

- **A market entry for «войти сейчас».** It fills at whatever the book gives: a short at MAGMA's 0.337 would have sold beyond
  the stop just the same, with slippage on thin pairs added. A limit at the card's entry already caps the price; what was missing
  is the check against the stop.
- **The rule in the strategy graph.** `LevelTrade` could compare the last close with the stop on start. Rejected for now: the
  close is up to a minute old when the entry is sent and the engine does not see the venue's last trade; changing the shared
  template also changes every owner's level trade strategy.
- **A tolerance in fractions of the risk.** Refusing an entry once the price has gone, say, 30 % of the way to the stop keeps
  the card's reward-to-risk. Not taken: a limit entry fills at its price or better, so only crossing the stop makes it lose at
  once; a pullback entry rests with the price half a risk above it by design.

## Consequences

- A trade whose market has crossed its stop or target between the card and the dispatch ends as failed with a reason the pair
  page names, and costs only its bot's start.
- A waiting entry claimed again after a deferral (ADR 0024) is checked again each time.
- Without a display price and with a stalled candle feed the entry is refused rather than sent blind.
- Other strategies may add guards through the same port; none does yet.
