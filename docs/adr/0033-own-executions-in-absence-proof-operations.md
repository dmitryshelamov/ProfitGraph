# ADR 0033: Own Executions in the Operations of the Absence Proof

Status: Proposed, 2026-10-27. Supplements ADR 0030 and ADR 0031; proven on the mock Invest API only. The owner reviews it in the
pull request.

## Context

ADR 0030's fifth check, `NoExecutionInOperations`, reads the account's operations on the share from five minutes before the
attempt was planned to now and counts every operation that is not cancelled with nothing executed as an execution. ADR 0030 named
the cost: a fill of any other order in the window blocks the proof. The real case (a GAZP bot, 2026-10-26): order 1, a market
purchase of 20 shares (2 lots), was executed at 18:50:37 UTC. Order 2 (a limit sale) was created at 18:50:37 and order 3 (a limit
purchase) at 18:51:39. Both windows, which start five minutes earlier, contain the fill of order 1, so the proof stopped on
`AutoCheck:ExecutionInOperations` although orders 2 and 3 executed nothing. Any order placed soon after another trade of the bot
is blocked the same way.

## What the API offers

Checked by reflection over `Tinkoff.InvestApi` 0.6.25 (the types the proof reads), not assumed:

- `OperationItem` has `Id`, `ParentOperationId`, `Type`, `State`, `Quantity`, `QuantityRest`, `QuantityDone`, `InstrumentUid`,
  `TradesInfo` and `ChildOperations`, among others. It has **no** `OrderId`, `ExchangeOrderId` or request ID, so an operation
  cannot be matched to an order by an order identifier. `ParentOperationId` ties a charge to its parent operation, not to an
  order.
- `OperationItem.TradesInfo.Trades[]` (`OperationItemTrade`) carries `Num` (the trade number), `Quantity`, `Price` and `Date`. The
  request option `WithoutTrades` was `true` in the module; it is now `false`, so the details come back.
- `OrderState.Stages[]` (`OrderStage`) of an order carries `TradeId`, `Quantity` and `Price` for each trade of the order.

The only identifier on both sides is therefore the trade: `OrderStage.TradeId` of an order and `OperationItemTrade.Num` of an
operation. That they are the same number is the assumption of this ADR, and it is **not checked on the real broker**. If it does
not hold, no operation is ever explained, and the proof behaves exactly as under ADR 0030 (it blocks); the failure is safe.

## Decision

The proof still counts an operation as an execution of the checked order unless the operation is cancelled with nothing executed,
or it is explained as the bot's own earlier trade. An operation is **explained** only when all of these hold:

1. Its type is `Buy` or `Sell` and its state is `Executed` or `Canceled` (cancelled after a partial execution). `Progress`
   (still executing), `Unspecified`, a state the module does not know, and every other type (fees, dividends, taxes, transfers,
   `Other` and the rest) are never explained. A commission or other movement of the share in the window therefore blocks, as before;
   the request still asks without commissions (`WithoutCommissions`), so a commission is normally not returned as an operation.
2. It has at least one trade, no trade without a number, and the trades' quantities add up to its `QuantityDone`. An operation
   without trade details, or whose details are incomplete, is not explained.
3. Every trade number is a trade of **one** engine order of the same bot that the bot recorded as executed on the same share in the
   window.

The known trade numbers come from the bot's own records and T-Invest, never from the operation being judged:

- The module lists the bot's attempts on the share with `executed_lots > 0` and `updated_at` not earlier than the window start
  (`ITInvestOrderAttempts.ExecutedOnShare`). An attempt's executed lots are written when it is final (ADR 0023), so an order still
  working with a partial fill is not listed and its operations stay unexplained.
- For each attempt that has an exchange order ID, the proof reads `GetOrderState` by that ID (the read ADR 0028 already uses) and
  takes the `TradeId` of its stages. The stages must add up to the executed lots the bot booked; an order T-Invest does not answer
  for, one without an exchange order ID, one whose stages disagree with the booked lots, and a stage without a trade number
  explain nothing. A trade number claimed by two different engine orders is dropped.
- The attempts of the engine order being checked never contribute. The checked order must have booked no execution anyway (check 2),
  and its trades are unknown by construction, so an operation that carries its trades stays an execution and blocks.
- No new column or migration: the trade numbers are read when they are needed, so orders executed before this change are covered
  too (the real GAZP order 1 has no stored trade IDs).

Not changed: the window (`attempt.CreatedAt - Skew`, `Skew` is 5 minutes) and `Skew` stay. The skew guards against clock
differences between this process and T-Invest and the exact moment `CreatedAt` is taken; excluding the bot's own trades makes the
reach of the window harmless instead of making it shorter, and a shorter window would admit a late-recorded fill of the order
itself. The position check (check 6: the account's position equals the engine's expected one, nothing blocked) stays as the second
guard and is the reason a wrong exclusion cannot close an order that really executed unless the position also matches.

Operations of other bots of the same account, of the owner's manual trades and anything without a known trade number are not
explained and block. That is deliberate: this ADR relaxes the rule only for what the bot itself booked.

### Edge cases

- **Partial executions.** A partial fill of the checked order is an operation with its trades unknown to the bot (nothing was
  booked, or the number is not in any other order's stages) and blocks, `Canceled` with `QuantityDone > 0` included. A partial
  fill of another order explains an operation only when that order has already ended and booked exactly those lots; while it still
  executes, its operation is `Progress` or its trades are unbooked, so it blocks.
- **Several orders in one second.** Matching is by trade number, never by time, price or quantity, so orders filled in the same
  second cannot be confused. An operation whose trades belong to two different orders is not explained.
- **Commissions and other movements.** Not explained; they block. An unrecognized operation type (the enum has more than 60
  values) blocks too.
- **Operation quantity units.** The trades' quantities are compared with the operation's `QuantityDone` in the operation's own
  unit; no conversion between lots and shares is made, so a unit mismatch can only make an operation unexplained.
- **Incomplete pages.** The read of more than five pages stays `OperationsIncomplete`; nothing is excluded from an unread page.

### The journal

When operations were excluded, the proof's passed checks gain one more code after `NoExecutionInOperations`:
`OwnExecutionsExcluded:{count}:{engine orders joined by +}`, for example `OwnExecutionsExcluded:1:1`. It holds only the number of
excluded operations and the engine order numbers (no keys, account data, trade numbers or amounts); the bot feed lists it with the
other checks and the page words it: "из операций по бумаге исключено собственных исполнений бота: 1 (заявки 1), остальных
исполнений нет".

## Consequences

- An order placed soon after another trade of the bot is no longer blocked by it, if the broker's trade numbers agree on both
  sides.
- One more read per own executed order in the window (`GetOrderState` by exchange ID), through the existing `Orders` limit. A
  failed read is a failed check (`ReadFailed`), as for every read of the proof.
- Not checked on the real broker: that `OrderStage.TradeId` equals `OperationItemTrade.Num`; that `GetOperationsByCursor` with
  trade details lists `Buy`/`Sell` operations with their trades; the unit of `Quantity` in the operation and its trades; that
  `GetOrderState` still answers by exchange order ID for an order filled the previous day. If any of these differs, the proof
  blocks instead of passing, and the owner reads the account as under ADR 0030.
