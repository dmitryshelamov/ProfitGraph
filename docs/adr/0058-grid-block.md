# ADR 0058: The Grid Block

Status: Accepted by the owner in the design interview of 2026-10-29 (questions 1–7). Sections 9 and 10 (live) are built in a
second pull request. Supplements ADR 0007 (single strategy format), ADR 0016 (live bot runtime), ADR 0048 and ADR 0051 (owner
cancel); uses the fill mode and metrics of ADR 0057.

## Context

A grid in the market-making sense, spot and long-only: many limit buys laddered below the price, each level with its own lot,
each bought level with its own limit sale at its entry price plus a profit step, and a level whose sale filled places its buy
again. Many orders are live at once. The existing Ladder, Ladder+ and Profit Fox DCA templates are averaging strategies: one
aggregated position and one exit from the average cost.

The block language cannot express a grid (checked in the engine on 2026-10-29):

1. A tag holds one active order (`OrderBook._activeByTag`) and a tag is a constant; there are no loops. N levels are N copies
   of blocks, and the 128-block limit (`StrategyAuthoringLimits.Blocks`) allows about ten to fifteen levels. The number of
   levels cannot be a parameter, so it cannot be optimized.
2. The position is one aggregate (`Portfolio`: quantity, cost basis, `averageCost`, `lastBuyPrice`). The fill fields exist in
   the event catalog (`Fill.BaseChange`) but a block's expressions cannot read an event, so the sale of level *i* cannot know
   what level *i* bought net of the fee: it either leaves dust or is rejected for missing base.
3. A cancel block cancels one tag; closing a grid takes N cancel blocks.
4. A bot starts without base, so only the buy side of a grid can exist unless something buys first.
5. One action node applies once per occurrence (`CausalDispatcher._applied`), so one node cannot place N orders on one event.

## Decision

### 1. A block kind of its own

A strategy gets a fifth block kind, `grid`, displayed as «Сетка». It has one event input and the exits `started` and
`stopped`. Its settings are value expressions like an order block's, each required:

| Setting | Type | Meaning |
|---|---|---|
| `levels` | Integer, 1–200 | Number of buy levels below the anchor. |
| `step` | Scalar (percent) | Distance between levels: level *k* is at anchor × (1 − step)^k. |
| `profitStep` | Scalar (percent) | A bought level's sale is at its entry price × (1 + profitStep). |
| `lot` | Quote amount | What each level's buy spends. |
| `maxOpenBuys` | Integer, ≥ 1 | At most this many of the grid's buys rest at once. Sales are not limited. |
| `shiftThreshold` | Scalar (percent), ≥ 0 | Upward shift trigger (section 5); 0 disables shifting. |
| `anchorCeiling` | Scalar (percent), > 0 | The shifted anchor never exceeds the first anchor × (1 + ceiling). |
| `stopBelow` | Scalar (percent), ≥ 0 | Stop below the grid (section 6); 0 disables the stop. |

Each setting must be static: a parameter or a constant, or arithmetic over them, never a candle, indicator, balance or
position (a validation error otherwise). Parameters make every setting optimizable, the number of levels included. The
settings are read at every evaluation, so a bot's parameter change (ADR 0025, ADR 0047) reaches a running grid at once
through the reconciliation of section 4: unfilled buys move, bought levels keep their lots.

The engine lowers the block to one `Grid` action node; its level state lives in the engine, not in the graph. A strategy may
hold several grid blocks; they share the account and their tags are distinct.

### 2. State and tags

The engine keeps, per grid node, a `GridState`: whether the grid runs, its first anchor and current anchor, and per level the
base it holds (net of the buy fee, summed over partial fills), the quote it paid and its active order. A level is free, buying
(its buy rests or is partly filled), or held (its buy ended with base, its sale rests or is due).

Each order of a grid has its own tag: `grid.<blockId>.<k>.buy`, `grid.<blockId>.<k>.sell`, and `grid.<blockId>.stop` for the
stop's market sale. The one-order-per-tag rule of the order book stays as it is. Global listeners and the bot page see the
tags like any other.

### 3. Start and anchor

An event arriving at the block's input starts a grid that does not run: the anchor is the price at which that event happened
(the start's open in a backtest, the last close for a live bot's start or resume), the first anchor is set to it, the exit
`started` fires, and the grid evaluates. An event arriving while the grid runs changes nothing. Starting by price is
therefore ordinary blocks (OnCandleClosed → condition → grid). The grid buys only below its anchor and starts without base;
a buy of initial base is not part of it.

### 4. Evaluation: desired orders, then the difference

On its start, on every fill, cancellation and rejection of its own orders, at every candle close and at every resume, the grid
computes the orders it wants and reconciles the book with them:

- Every held level wants its sale: quantity = the level's base rounded down to the quantity step, price = the level's average
  buy price × (1 + profitStep) rounded up to the tick. A buy partly filled and then cancelled leaves a held level with the
  part it bought.
- The free levels whose price is below the evaluation's market price want a buy of `lot`, highest first, until
  `maxOpenBuys` buys are wanted (buying levels count). A buy the account cannot fund is not submitted.
- A resting grid order that is not wanted, or wanted at another price, is cancelled; a wanted order whose tag is free is
  submitted. A higher level freed by a sale therefore displaces the lowest resting buy when the limit is reached.
- A level whose buy or sale was rejected is not retried before the next candle close, so a rejection cannot cascade.
- A level's sale that filled frees the level; base left by rounding is released from the level and stays in the account.

Iteration goes by level index and each submit or cancel is its own action request of the grid's node, so one occurrence can
place many orders, in a fixed order, and a backtest stays deterministic. The cascade guard applies as for any action.

### 5. Upward shift

At a candle close, when the grid runs, no level is held or buying with base, `shiftThreshold` > 0, the close is above the
anchor × (1 + shiftThreshold), and the close is at most the first anchor × (1 + anchorCeiling), the anchor becomes that close
and the evaluation moves every buy to the new levels. At most one shift per candle. The grid never shifts down; a fall below
it is the stop's matter.

### 6. Stop below the grid

At a candle close, when `stopBelow` > 0 and the close is below the lowest level's price × (1 − stopBelow), the grid stops: it
cancels all its orders and, once none is active, sells at market the sum of its levels' base (rounded down to the step and at
most the available base, never base it did not buy), then clears its levels, stops and fires `stopped`. It does not start
again by itself; the next event at its input starts it with a new anchor. A whole-grid take profit is not part of this ADR:
each level takes its own profit.

### 7. Stopping a grid from the graph

A cancel block may name a grid block instead of a tag. Cancelling a grid cancels all its orders and stops it without selling:
its levels' base stays in the account for the strategy's other blocks. Its completion fires when no grid order is active.

### 8. Fee check

The engine offers a pure check of a grid's static settings against the run's or the bot's fees:

- Error: `profitStep` ≤ 2 × the limit fee rate. Every round trip loses; a backtest, optimization or walk-forward is refused,
  and a bot neither starts nor takes such parameters.
- Warning: `step` or `profitStep` below 2 × the limit fee rate + slippage, a margin for the stop's market sale and the tick
  rounding. The form shows it and the run records it as a diagnostic; nothing is refused.
- An optimization candidate that fails the error check is not replayed and spends no budget, like a candidate the template's
  parameter rules reject (`RejectedByTemplate`), with its own diagnostic.
- With seller fees per unit (ADR 0022) the check takes the effective rate at the run's first replay close or the bot's last
  close. A venue's fixed minimum commission (such as T-Invest's) is not part of the formula.

### 9. Live: venues that can hold a grid

A bot whose strategy contains a grid starts only on a venue whose module provides the port that states how many resting
orders one bot may keep on an instrument (`IVenueRestingOrderCapacity`). Without the port the start is refused with
`grid_not_supported_by_venue`; with it, `maxOpenBuys + levels` must not exceed the capacity (`grid_exceeds_venue_orders`).
In this work the Bybit module (its limit from configuration, to be checked against Bybit's documentation before trading) and
the scripted venue provide it; T-Invest (DAY orders carried over every night by ADR 0024 and ADR 0031) and Steam (one buy
order per item, which replay does not model, ADR 0022) do not, until a step of their own proves their scenarios. A backtest on
a venue without the port shows a warning that replay assumes unlimited resting orders. Live trading stays disabled by default.

### 10. Live: checkpoint, resume and the owner's actions

- The engine checkpoint carries every grid's state (`GraphEngineCheckpoint.Grids`), so a restarted bot continues with its
  anchors and its levels' lots; a checkpoint without the field has no grid.
- A resume (restart, graph change, the reconciliation's extra resume of ADR 0035) evaluates every grid: orders the venue
  removed meanwhile are placed again, filled ones are booked to their levels first.
- An owner's cancel of a grid order (ADR 0048) or a venue's cancel nobody asked for (ADR 0039) ends that order; the grid's
  evaluation places it again, as ADR 0051 resumes any strategy. Stopping a grid is the cancel block of section 7 or stopping
  the bot.
- The kill switch cancels the bot's orders as for every bot and never sells; the grid's levels stay in the checkpoint.
- An owner's manual order (ADR 0046) carries the `manual` tag and belongs to no level; the grid never sells base it did not
  buy. The Ladder's manual fill reaction (ADR 0037) does not apply to a grid.
- Each grid buy is checked against the bot's per-order limit (ADR 0018) like any buy.

## Considered alternatives

- **A template factory that copies blocks per level.** Cheap, but blocked by the 128-block limit, the number of levels is not
  optimizable, and a level's lot still needs new expressions.
- **General language primitives** (indexed tags, loops, event fields in expressions, cancel by tag prefix). Most general and
  most expensive; a grid would be a template on top. Rejected for now; the grid block does not prevent them later.
- **An absolute price range** as on Bybit (lower and upper price). Tied to one market and one moment; over a year of history
  the price leaves the range and the grid stands still, and optimization or walk-forward means nothing. Rejected in favour of
  an anchor and percentage steps.

## Consequences

- The engine gains a stateful action and a checkpoint field; the editor gains a block kind with its own inspector.
- A grid is comparable with Bybit's grid bots only through ADR 0057's `Through` fills and capital cost metrics; the form's
  default makes that the normal case.
- MOEX shares and Steam items can be backtested with a grid but not traded by one until their own step.
- Showing the grid's levels on the bot chart is a later step.
