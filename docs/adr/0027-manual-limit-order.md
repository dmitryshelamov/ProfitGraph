# ADR 0027: Manual Limit Order

Status: Proposed, 2026-10-26. Built with this pull request for all three venues. The owner delegated the decisions below
while away and reviews them in the pull request.

## Context

The bot page sends an owner market order (Profit Fox Designer ADR 0091, ADR 0016 amendment "manual orders"): a request row
in `bot_manual_orders`, taken by the runner that holds the bot's lease as a root, submitted to the engine with the reserved
tag `manual` and origin `$manual`, and from there on the normal path: order intent, per-order limit, gateway,
reconciliation, fills and the feed. The owner wants a limit order in the same block.

What the code already does, so that nothing is duplicated:

- The engine takes limit orders (`OrderType.Limit`) from graph blocks. Its account rounds the price down to the tick and the
  quantity down to the step, rejects below the minimums and without enough free quote or base, and reserves funds. A buy
  limit's amount is a quote budget (`quantity = floor(amount / price)`), a sell's is base, as for the market order.
- All three gateways already place engine limit orders: Bybit (`BybitOrderRequests`, `orderType Limit`), T-Invest
  (`TInvestOrderPlans`: whole lots, price on the tick, `PostOrder` limit with the day time in force, price band, session
  waits of ADR 0023) and Steam (limit buy order, sell listing at the price after the seller fee, trade-hold waits of ADR
  0024). Waiting orders (ADR 0024) and open orders already show a resting order on the bot page.
- The `manual` tag admits one working order, so a resting manual limit makes the next manual order a `TagActive`
  rejection.
- The engine has no owner-initiated cancel of an order that is not tied to a graph block (ADR 0024, stage 3, "not built").

## Decision

### 1. What a manual limit order is

A manual limit order is the same explicit owner action as the market one, with a price. `POST /api/bots/{id}/manual-orders`
takes an optional `type` (`Market`, the default, or `Limit`) and, for a limit order only, `limitPrice`; a limit order without a
price, or a market order with one, is a 400. Amount keeps its meaning: quote for a buy, base for a sell. The request row gets
`order_type` and `limit_price` (migration `202610260005`; existing rows are `Market`). The price is stored as typed; the
engine rounds it down to the tick. Nothing in the strategy, the desired state or the bot's settings changes.

### 2. Ownership and lifetime

- **Owner.** The order belongs to the owner's request, not to a block. It is submitted by the runner as a root, exactly like
  the market order, so the engine's account, the risk limits and the position ledger include it, and the graph sees it only
  through global listeners filtered by tag `manual` (ADR 0091). A graph change keeps it (the orphan sweep skips `$manual`).
- **Request.** Only a bot that should run and runs takes a request (409 `bot_not_running`), at most one is pending (409
  `manual_order_pending`), and one nobody takes in 60 s is `expired`. A request never trades after the bot stopped.
- **Resting order.** After the root the order is an ordinary engine order. Stopping the bot leaves open orders (ADR 0016) and
  the order stays at the venue; a restart restores it from the checkpoint and reconciliation follows it. A stop is not a cancel.
- **Partial fill.** Handled by the engine and the gateway as for any limit order (`PartialFill` updates, fills in the
  ledger); the order stays working until it ends.
- **Divergence from the strategy.** The strategy is not consulted and does not cancel it. Both can hold orders: the manual
  order reserves its funds, so the strategy's later orders see less free quote or base. This is the owner's decision.
- **Cancel.** Not built, see the next steps.

### 3. Idempotency

The request id identifies the action: the unique index allows one pending request per bot, so a repeated POST (double click,
retry) while the first waits is a 409, never a second order. The root resolves the request and saves the checkpoint and the
order intent in one transaction, and the venue client order id derives from the bot and the engine order id (ADR 0017,
ADR 0022, ADR 0023), so a retried delivery or a restart cannot place the order twice. A POST after the first request
resolved is a new explicit action; if the first is still resting, the engine refuses it with `TagActive`.

### 4. Validation

Two layers; the backend is the authority, the form only avoids avoidable rejections.

- **API and use case.** Exact decimal strings; amount and price are positive; the type matches the price.
- **Engine (as for graph orders).** The price is rounded down to the tick (`DecimalRounding.FloorToStep`) and the quantity
  to the step; a T-Invest share's step is its lot (GAZP: 10 shares), so a limit sell of 37 is 30 and a limit buy of 3000 at 150
  is 20. A quantity of zero is `ZeroQuantity`, below the minimums `BelowMinQuantity`/`BelowMinNotional`, a price of zero after
  rounding `InvalidPrice`, not enough free quote or base `InsufficientQuote`/`InsufficientBase`. The bot's balance is its
  engine account (the allocation), not the venue wallet.
- **Risk.** The order follows the per-order limit (ADR 0018), the exit exemption and the sell price floor (ADR 0024) and the
  kill switch, because it takes the same intent path.
- **Direction relative to the market.** Not rejected. A limit that crosses the market is a marketable limit and the venue
  fills it as taker; T-Invest moves the effective price into the band (ADR 0023). The sell price floor guards a limit sell far
  below the market. The confirmation says the order will rest on the venue.
- **Form.** The instrument rules (`/api/market-data/instrument-rules`) give the tick, step, minimums and precision. For a limit
  order the form refuses a price off the tick (rounding a typed price silently would surprise the owner), shows the quantity
  after rounding down, and refuses what rounds to zero or falls below the minimums. A rejected order shows its reason in
  Russian text.

### 5. Venues

| Venue | Limit manual order |
|---|---|
| Bybit Spot | Supported: limit order with `orderLinkId`, price and quantity rounded to the instrument. |
| T-Invest | Supported: whole lots, price on the tick, day order; waits (`SessionClosed`, `PriceBand`, carry-over) show as waiting orders. |
| Steam | Supported: a limit buy is a buy order, a limit sell is a listing at the raised-to-representable price; trade-hold and single-slot waits (ADR 0024) apply and show as waiting orders. |

No venue refuses it, so the form hides nothing. A venue added later whose gateway cannot place limit orders must reject
`OrderType.Limit` as `BrokerRejected` with a stable code; the form then shows that reason. No capability flag is added until
such a venue exists.

### 6. Form

A "Рыночная/Лимитная" selector, a price field for a limit order, a hint with the tick, the lot and the minimum, the
quantity after rounding, a confirmation step ("Подтвердить и отправить" / "Назад") before a limit order is sent, and the
status of the request (waiting, handed over, rejected with the reason, with the limit price). A market order is sent as before.
The chart is not touched: the placed order reaches it through open orders.

### Next steps (not built)

- **Cancel of a manual order.** An owner action `DELETE /api/bots/{id}/manual-orders/{orderId}` processed as a root on the
  runner's queue (`CancelByTag("manual")`, then the normal cancel intent), like the manual order itself. It also covers the
  gap that a resting limit blocks further manual orders until it fills. Until then the owner cancels at the venue.
- Showing the manual order in the open-orders block with a "ручная" mark.

## Consequences

- One more column pair and one new factory on the aggregate; the transitions of `ManualOrderRequest` do not change.
- A manual limit order that never fills stays until the owner cancels it at the venue, and blocks other manual orders. The PR
  and the form say so.
- Nothing was verified against a real venue: the gateways are covered by their mocked tests and the pipeline by the
  integration tests with a mocked gateway.
