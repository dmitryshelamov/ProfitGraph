# ADR 0061: Bybit Futures and Short Trades

Status: Proposed, 2026-10-30. The owner asked for the next scanner step: Bybit USDT perpetuals (category `linear`) as a scanner
source and a trading venue, and short entries, so that a futures pair's page shows short ideas next to the long ones and creates
trades from them. The decisions marked **(owner)** below are open questions for the owner; the rest are proposals reviewed in the
pull request. Builds on ADR 0003, ADR 0016, ADR 0017, ADR 0030, ADR 0044, ADR 0059 and ADR 0060.

## Context

Everything that trades is spot today. The engine's account holds a quote balance and a base balance that never goes below zero:
a sale reserves base it holds, a buy reserves quote (`Portfolio`, `TradingAccount`, `ExternallyExecutedAccount`). The Bybit module
sends `category=spot` everywhere: klines, instruments, tickers, fee rates, orders, cancels, the private stream topics
`order.spot` and `execution.spot`, and the absence proof compares the base coin's wallet balance with the engine's position
(ADR 0030). The scanner's ideas (ADR 0060) are long only: entry at the price, stop at the support, target at the resistance or two
risks above.

A short needs a venue where the bot can sell what it does not hold. Bybit's USDT perpetuals settle in USDT from the same Unified
Trading Account as spot: a position is a signed quantity of contracts in base units, profit and fees are paid in USDT, funding is
exchanged every few hours, and the account's margin mode and position mode decide how orders net.

## Decision

### 1. A second Bybit venue, `bybit-linear`

- **Venue.** `bybit-linear`, «Bybit Futures», market «USDT Perpetual», a second `IVenueDescriptor` in
  `ProfitGraph.Venues.Bybit` beside `bybit` (spot). Only linear perpetuals that settle in USDT and are `Trading` are listed; the
  symbol is Bybit's (`BTCUSDT`), the same text as spot, but candles, instruments, credentials, bots and scanner snapshots are keyed
  by the venue, so spot and futures never mix. The descriptor says the venue allows shorts (`SupportsShort`, a neutral flag);
  spot does not.
- **One module, two categories.** The spot classes take a category (`spot` or `linear`) instead of the constant: historical and
  live klines (`/v5/market/kline`, public stream `wss://…/v5/public/linear`), instruments (`/v5/market/instruments-info`:
  `priceFilter.tickSize`, `lotSizeFilter.qtyStep`, `minOrderQty`, `minNotionalValue`), the catalog, tickers and the scanner source
  (`BybitMarketScanSource` with its own REST budget share), fee rates (`/v5/account/fee-rate?category=linear`). Both venues pay
  every fee in quote (`FeeAsset.Quote`).
- **Credential (owner).** A key is enrolled for `bybit-linear` like for spot; the owner may enroll the same Bybit key again. It
  needs the «Contract → Orders, Positions» permission; `KeyMayTrade` checks it. *Recommendation: a separate credential of the same
  key, because credentials, accounts and account cards are keyed by venue (ADR 0003); sharing one credential across two venues is
  a core change for little gain.*

### 2. Positions on the venue: hedge mode, 1× leverage, reduce-only exits

- **Position mode (owner).** The page shows long and short cards of one pair, and the owner may tick both (ADR 0060 allows several
  trades on one pair). In Bybit's one-way mode those trades net into one position, and a reduce-only exit of one trade is rejected
  when the other trade's opposite position cancelled it out. *Recommendation: hedge mode (`BothSide`) for USDT perpetuals.* Long
  orders go to `positionIdx=1`, short orders to `positionIdx=2`; every exit is `reduceOnly=true`, so it can only shrink its side and
  never opens the opposite one when the venue holds less than the bot thinks (the owner closed it by hand, auto-deleveraging). The
  bot does not switch the mode: the owner switches it once (in Bybit or by one authorized call to `/v5/position/switch-mode`
  with `coin=USDT, mode=3`), and reconciliation reads `/v5/position/list` and stops the bot with `VenuePositionMode` when the symbol
  is in one-way mode. *Alternative: one-way mode, `positionIdx=0`, reduce-only exits, and a 409 `scan_trade_opposite_live` that
  forbids a long and a short trade on one pair at once.*
- **Leverage (owner).** *Recommendation: 1× only.* Before a bot's first order on a symbol the gateway sets
  `buyLeverage=sellLeverage=1` (`/v5/position/set-leverage`; `110043`, "not modified", is success). The engine reserves the whole
  notional of an opening order as margin, as spot reserves the quote of a buy, so a bot never uses more than its allocation and a
  short's liquidation price is about twice its entry. The bot does not touch the account's margin mode (cross by default in a
  Unified account); under 1× it does not change the bot's risk.
- **Orders.** `category=linear`, `positionIdx` as above, `reduceOnly` on exits, `timeInForce=GTC` for limits; a market order is
  sized in base (`marketUnit` is spot only). The minimum checks use the linear rules; a market exit's notional is checked at the
  ticker's last price, as on spot.
- **Private stream and reconciliation.** The linear bot subscribes to `order.linear` and `execution.linear`. Only executions of
  type `Trade` belong to orders; `Funding` executions are not foreign orders and are ignored (section 5). The order lookups use
  `category=linear`. The absence proof (ADR 0030) compares the size of the bot's side (`/v5/position/list`) with the engine's
  position instead of the base wallet; as on spot, a side that also holds the owner's or another bot's contracts fails the check
  and leaves the order with the owner.
- **Simulation.** `BybitSimulatedOrderGateway` serves `bybit-linear` too, so the end-to-end tests and the Worker without
  `Live:Trading:Enabled` trade it without requests.
- **Real orders** only with `Live:Trading:Enabled` in a Worker with the live role (ADR 0017); every committed configuration keeps
  it false.

### 3. The engine: a run is long or short

- **Direction.** A strategy definition declares its direction, `Long` (default, omitted from the stored JSON and the fingerprint,
  so every existing strategy, backtest and pinned hash is unchanged) or `Short`. A short strategy is valid only on a venue that
  `SupportsShort`: a backtest, an optimization or a bot of it on spot is refused with `strategy_short_unsupported`. A run never
  flips: a long run cannot go below zero, a short run cannot go above zero.
- **Short accounting** (`Portfolio`, `TradingAccount`, `ExternallyExecutedAccount`). The base balance may be negative in a short
  run; equity stays `quote + base × close`, so the result, drawdown and every metric need no second formula. A sale that opens or
  adds to the short reserves its notional plus fee in quote as margin (rejected with `InsufficientQuote` beyond it, `RiskLimit`
  beyond the allocation); its proceeds are credited to quote and its fee charged. A buy reduces the short and reserves the short
  quantity it covers (rejected with `InsufficientBase` beyond the uncovered short); its cost is debited and its fee charged.
  Realized profit of a cover is the proceeds share of the covered quantity minus its cost and fees, as a sale's is today. The
  checkpoint stores the direction; a checkpoint without it is long.
- **Blocks.** `position.quantity` is negative in a short run; `PositionFlat` and the flat preview compare its absolute value with
  the minimums. A new balance field `shortAvailable` is the uncovered short quantity, the counterpart of `baseAvailable`; the order
  preview checks a short sale against the quote margin. A sale's `amount` stays a quantity; a short entry by quote amount is
  `amount / price` with the existing arithmetic.
- **Positions and events.** A short run opens a position cycle on its first sale and closes it when it is flat again
  (`BotPositionOpened`, `BotPositionClosed`, ADR 0021), with the realized profit of the cycle; the bot page shows the position as
  short.
- **Backtest.** A short strategy backtests on `bybit-linear` history with the same fill rules (ADR 0057); funding is not
  simulated.

### 4. `LevelTradeShort`, «Сделка по уровням, шорт»

A second template, the mirror of `LevelTrade`: on start a limit sale of `sellAmount / entryPrice` at `entryPrice`; when the entry
fills (or is cancelled after a partial fill), a limit buy of `shortAvailable` at `targetPrice`; when a closed candle's close is at
or above `stopPrice`, the entry and the target are cancelled and the short is covered by market. Its rules require
`targetPrice < entryPrice < stopPrice`; the tags are the same (`level.entry`, `level.target`, `level.stop`). The first short trade
creates «Сделка по уровням, шорт (сканер)», as ADR 0060 does for the long one. A separate template keeps both graphs simple and
lets each be edited on its own; a direction parameter would put a branch into every block.

### 5. Scanner: short ideas and a short backtest

- **Source.** The `scanner` Worker role scans `bybit-linear` beside `bybit`, with its own universe, snapshot and presets; the
  scanner page chooses the venue as today.
- **Ideas.** `ScanTradeIdeas` returns a direction with each idea. On a venue that `SupportsShort` the mirror scenarios join the long
  ones on every level timeframe:

  | Scenario | Entry (limit sale) | Stop | Target | The entry waits |
  |---|---|---|---|---|
  | `Levels` short | the price | the resistance | the support | 1 hour |
  | `Target2R` short | the price | the resistance | two risks below the entry (above zero) | 1 hour |
  | `Pullback` short | halfway up from the price to the hour's highest high | that high | two risks below the entry | 4 hours |

  `Levels` short needs a resistance above and a support below the price, `Target2R` short a resistance above, `Pullback` short an
  hour that fell. The amount is `risk / ((stop − entry) / entry + 2 × fee)`, with the same `ScanTrades:FeeRate` of 0.1 % a side
  (higher than Bybit's linear fees, so the loss at the stop stays under the risk).
- **Backtest of a preset** (`ScanStudy`, ADR 0059 section 8). On `bybit-linear` every signal also simulates the three short
  scenarios, with the stop on a minute close at or above it; the summary shows each scenario per direction.
- **Trades.** `ScanTrade` keeps its direction (a `direction` column, `long` for existing rows); the uniqueness of a live trade
  includes it; the settings are per owner and venue, so `bybit-linear` has its own credential, risk and largest amount; the
  tracker treats a non-zero position of either sign as held. The guards of ADR 0060 section 4 apply unchanged.
- **Cards.** The pair page of a futures pair shows long and short cards per timeframe, each marked «Лонг» or «Шорт» with its
  colour, and the totals of «Создать выбранные» count both. A spot pair shows long cards only, as today.

## Alternatives

- **One signed account without a direction.** A run could go long and short in turn, but then every order needs a position
  effect (open or reduce), an order that crosses zero must be split or refused, and every spot strategy would have to prove it
  never shorts. A fixed direction per strategy covers the cards and keeps spot unchanged.
- **Direction as a parameter of `LevelTrade`.** One template for both, but every block would branch on the parameter, and an
  optimization could flip a run's direction.
- **Bybit's attached take profit and stop loss.** The same reasons as ADR 0060; reduce-only exits on the bot's order path keep one
  reconciliation.
- **Margin trading on spot** (`isLeverage=1`). Shorts on spot borrow the coin with interest and their own liquidation rules; USDT
  perpetuals are Bybit's main short market with the liquidity the scanner sees.

## Consequences

- Futures pairs are a second market in the scanner, the charts, backtests and bots; spot behaviour, fingerprints and pinned
  replay hashes stay byte-identical.
- Funding is neither simulated nor booked: a trade's result on the bot page leaves out the funding Bybit charges or pays while the
  position is open, at most a few settlements for a one-day trade. The account card's wallet shows it.
- Hedge mode is an account setting the owner keeps; their manual futures trading in Bybit then also has two sides per symbol.
- With 1× leverage a futures trade ties up as much USDT as a spot trade of the same amount; spot and futures bots share the Unified
  account's USDT.
- The stop stays software on minute closes (ADR 0060): it can slip and does not act while the Worker is down.

## Implementation order

1. Engine: direction, short accounting in replay and external accounts, blocks, `LevelTradeShort`, tests.
2. Bybit `linear`: descriptor, market data, instruments, fees, scanner source; then the order gateway, stream, reconciliation and
   absence proof on the mock exchange.
3. Scanner: short ideas and backtest, `ScanTrade` direction, cards with direction; integration and app tests.
4. One authorized real check after the owner's go-ahead: a minimal short and its cover on a liquid pair with
   `Live:Trading:Enabled` on the owner's Worker only.
