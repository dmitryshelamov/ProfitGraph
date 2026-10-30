# ADR 0065: Scanner Trades Are Sized With the Bot's Fees and the Spread

Status: Accepted, 2026-10-30. The owner asked for it ("Комиссии и спред") after a production trade of a scanner card. Proven on the
test venue, engine and domain unit tests and the frontend's fake API; no real venue was asked for fee rates by the tests.

## Context

A scanner trade card (ADR 0060) sized its amount as `risk / ((entry − stop) / entry + 2 × fee)` with `ScanTrades:FeeRate` of
0.1 % on each side and no spread. The trade's bot pays the venue's rates for the owner's credential, read when it is created:
its limit entry and target pay the maker rate, and its stop sells at the market, paying the taker rate and crossing the spread.

On 2026-10-30 a Bybit Spot card on TRXUSDT (Target2R 5m, entry 0.356, stop 0.35535, target 0.3573, amount 30) reached its
target for +0.049 USDT: fees took more than half of the move, and the nominal 2R paid 0.43R. Its bot's market rate was 0.18 %,
so a stop would have lost about a fifth more than the card showed. The stop was 0.18 % from the entry, nearer than the round
trip's fees.

## Decision

1. **Costs.** `ScanTradeCosts` (Engine) holds the limit fee rate, the market fee rate and the spread, as fractions. A stopped trade
   pays the limit rate, the market rate and the spread on its amount (`AtStop`); a trade that reaches its target pays the limit
   rate twice (`AtTarget`). The amount is `risk / (|entry − stop| / entry + AtStop)`, capped at the largest amount, which then
   risks `Loss(maxAmount)`; the profit at the target is `amount × (|target − entry| / entry − AtTarget)`.
2. **The bot's rates.** `ScanTradeCostsReader` reads the venue's maker and taker rates of the pair with the owner's settings'
   credential through the bot's own port (`IVenueFeeRateReader`, as `CreateBot` does) and keeps them for
   `ScanTrades:FeeRatesLifetime` (15 minutes) in a host-wide cache keyed by credential, venue and pair. Without settings, a
   credential, a port or an answer, it assumes `ScanTrades:FeeRate` for both and says so (`feesFromVenue: false`).
3. **The spread.** The pair's snapshot row gives the spread (ADR 0059, in percent); without the row it is taken as zero and the
   response says it is unknown. The whole spread is charged to a stop, since the bot's market order crosses it from a close.
4. **Too tight.** An idea whose stop is nearer the entry than `AtStop` is left out of the cards; the response counts them
   (`tightStops`), and creating such a trade is refused with `scan_trade_stop_too_tight`. Creating a trade sizes it with the
   same costs and, on a margin venue, reserves the margin plus a limit and a market fee.
5. **The response.** `GET /api/scanner/{venue}/trade-ideas` adds `costs` (limit and market rates, spread, where they came from),
   `tightStops`, and each idea's `profit` after fees. The page shows the costs under the table and takes the profit from the
   response instead of deriving it from the risk.
6. **Presets.** Every built-in Bybit preset (spot «Импульс», futures «Импульс» and «Падение») asks for a spread of at most 0.1 %,
   as «Под лесенку» did. The preset backtest still ignores spread conditions (ADR 0059, section 8) and keeps its flat 0.1 % fee.

## Consequences

- Opening a pair asks the venue for its fee rates with the owner's credential at most once per 15 minutes per pair and host; the
  call is the same read-only request a bot's creation makes.
- Cards size a little smaller where the market rate is above the limit rate or the spread is wide, and fewer ideas show on
  calm pairs with near levels.
- The backtest and the cards no longer assume the same costs; bringing real fees and spread into the backtest needs quote
  history the scanner does not keep.
