# ADR 0063: Entry Fees in Bot Reports

Status: Accepted, 2026-10-30. The owner chose the reporting-only variant below.

## Context

A buy pays a fee: on Bybit spot in base (0.1 BTC bought, 0.0999 BTC credited), on T-Invest in quote. The engine's portfolio
puts the whole amount spent into the position's cost basis (`Portfolio.Buy`, `Portfolio.BuyWithQuoteFee`), so the fee
becomes part of the average cost and turns into realized profit only when the position is sold. The total of a closed
position is right, but while it is open the bot page shows a higher average price than the fills had, an unrealized profit
lowered by the fee and a realized profit that does not show the fee already spent.

The owner asked to count a buy's fee as realized at once and to show fees in quote. The cost basis is also what strategies
see: `position.averageCost` and `position.costBasis` place ladder exits (for example «average cost + 0.3 %»), and the backtest
metrics read the realized and unrealized profit. Moving the fee out of the cost basis in the engine would lower every exit by
the buy fee, change live bots' trading on deploy and make new backtests disagree with stored ones.

## Decision

1. **The engine trades as before.** The cost basis, `averageCost`, `realizedPnl` of the position node, `Fill.RealizedPnl`,
   fill notifications, position cycles and backtest metrics are unchanged.
2. **The portfolio remembers the entry fees its cost basis holds.** `Portfolio.FeesInCost` grows by a buy's base fee valued
   at its fill's price (`quoteSpent × fee ÷ grossBase`), by a buy's quote fee as it is, by a short entry's fee and by the
   retained part of a late quote fee correction (ADR 0023). A sale or a cover takes the same share of it as of the cost basis.
   It is kept in `ExternalAccountCheckpoint.FeesInCost`; a checkpoint written before has none, so a position open at deploy
   shows the old figures until it closes. No data migration.
3. **The bot's position reports entry fees as spent.** `GET /api/bots/{id}/position` keeps `costBasis` and `averageCost`
   (the break-even price, fees included) and adds:
   - `entryFees`: the fees inside the cost basis;
   - `averageBuyPrice`: (cost basis − entry fees) ÷ base, the price the fills had;
   - `realizedPnl`: the engine's realized profit − entry fees, so a buy's fee shows as a realized loss at once;
   - `unrealizedPnl`: base × price − (cost basis − entry fees), with the short formula of the portfolio for a short;
   - `positionFees`: every fee of the open cycle in quote (base fees at their fills' prices);
   - `cycles[].fees`: the same for each listed cycle.
   A cycle's fees are its fills' fees from its opening through its closing. A cycle opens on its candle's grid, so a range
   starts just after the previous cycle closed and a fill counts in one cycle only. A closed cycle's realized profit already
   holds every fee of the cycle and is unchanged.
4. **The feed carries a fill's fee.** `BotFillRecorded` adds `Fee` and `FeeAsset`, and the feed's fill entry stores `fee` and
   `feeAsset`. An outbox message written before has neither and the page shows no fee for it.
5. **The page shows fees in quote first.** A base fee is valued at its fill's price with the base amount after it,
   «0.01 USDT (0.0001 BTC)», in the fills table and the feed. The position card shows the average buy price with the
   break-even price under it, the realized profit «с комиссиями покупок», the position's fees and each cycle's fees.

## Consequences

- The bot page and the engine show different «average» prices on purpose: the card's average buy price leaves the fee out,
  the strategy's `averageCost` (the card's break-even price) keeps it.
- The sum of a position's reported realized and unrealized profit equals the engine's; only the split moves.
- Backtest reports keep the engine's split. Showing entry fees as spent there too is a separate change if it is wanted.
- A sale's realized profit in a fill notification still holds the buy fee of what it sold, so it is lower than the position
  card's realized profit moved by that sale.
