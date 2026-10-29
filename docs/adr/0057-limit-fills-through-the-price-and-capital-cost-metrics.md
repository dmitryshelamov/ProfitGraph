# ADR 0057: Limit Fills Through the Price and Capital Cost Metrics

Status: Accepted by the owner in the design interview of 2026-10-29 (questions 5 and the grid's metrics). Supplements
ADR 0012 (backtest runs), ADR 0013 (optimization) and the engine's OHLC execution model. Prepares the grid of ADR 0058.

## Context

The engine fills a resting limit order completely as soon as the simulated price path reaches its price: a buy at 100 fills on
a candle whose low is exactly 100 (`OrderMatcher`: `price <= limit`, and a descending segment fills when `limit >= end`). On a
venue a resting order at the touched price stands in a queue and often stays unfilled, so the backtest overstates every
strategy that lives on limit orders. A grid of ADR 0058 lives on nothing else: each level is a limit buy and a limit sale a
small step apart, and the number of round trips is the result. A touch fill model therefore inflates exactly the number the
owner wants to compare against Bybit's free grid bots.

The run's metrics also hide what a grid costs. Return, drawdown and fees say nothing about how much capital stood in the
market, how long the strategy held a position, or how long that position was worth less than it cost. A grid that earns 5 %
with all its capital locked under water for eight months of twelve is a different strategy from one that earns 5 % while
mostly flat.

OS Engine's tester has the same choice (`OrderExecutionType.Touch` and `Intersection` in `TesterServer`, plus an alternating
`FiftyFifty`). Its licence forbids derivative software; it is used here only as a checklist.

## Decision

### 1. A run setting: limit fills on touch or through the price

`ExecutionSettings` gets `LimitFill` with two values, set like the settlement hold (`WithLimitFill`):

- `Touch`: today's rule, unchanged. A buy fills when the price is at or below its limit, a sale at or above.
- `Through`: the price must trade strictly beyond the limit. A buy fills only when the path goes strictly below its limit, a
  sale only strictly above; a candle whose low equals a buy's limit leaves it resting. The fill price stays the limit, as
  today, also when the order is already marketable at a gap. The whole remaining quantity fills, as today (Full Limit
  Fill); partial fills are not modelled.

Both are deterministic functions of the candle path Open → Low → High → Close. The alternating `FiftyFifty` mode is not
adopted: it makes one candle's fill depend on the previous order's outcome and adds nothing a pessimistic bound does not.

`LimitFill` applies to every strategy and to replay only: backtests, optimization trials and walk-forward
windows. Live bots never read it; the venue fills their orders.

### 2. Defaults and frozen runs

- A new backtest, optimization or walk-forward request defaults to `Through`; the form shows the choice beside slippage and
  fees ("Исполнение лимитов: касание / пробой"). The API accepts the field and defaults an omitted one to `Through`.
- A run's frozen configuration stores the mode. A stored configuration without it is read as `Touch`, so every existing run,
  its results and its comparisons keep their meaning; rerunning a stored run reproduces it.
- The engine's own default stays `Touch`, so engine tests, regression fixtures and pinned replay hashes are unchanged; only the
  application's request default moves.
- The execution policy version (`BacktestResult.ExecutionPolicyVersion`) does not change: the mode is a recorded input, not a
  change of the existing rules.

### 3. Capital cost metrics

Every sample of a run's metrics (full, in-sample, holdout) gains, for all strategies:

| Metric | Definition at each observed candle close |
|---|---|
| Max capital deployed | The largest cost basis of the held position (quote paid for the base still held), in quote and as a percentage of starting capital. |
| Max capital committed | The largest cost basis plus quote reserved by resting buys: what the strategy needed at once. |
| Time in position | The share of observed closes at which the position was not flat by the engine's flat rule (unsellable dust counts as flat). |
| Time under water | The share of observed closes in a position at which the held position was worth less than its cost basis (unrealized result below zero). |
| Longest under water | The longest run of consecutive such closes, in candles. |

They are computed from the same completed-candle observation as the other metrics, so a holdout sample starts its own maxima
and shares. A stored run's metrics without them show "—"; nothing is recomputed. Optimization trials keep the columns they
store today; storing these metrics for trials and ranking by them are a later step.

## Consequences

- A Ladder or a grid backtested with the default `Through` shows fewer fills than the same run with `Touch` on the same
  candles. That is the intended correction; comparing an old and a new run shows the mode of each.
- `Through` still assumes the whole order fills once the price trades through it; thin markets remain optimistic. The setting
  is a lower bound on optimism, not an order book model.
- The new metrics add fields to the run's stored metrics and to their HTTP contracts; older rows read them as absent.
