# ADR 0017: Bybit Spot Order Gateway

Status: Accepted, 2026-10-25.

## Context

Live bots deliver their order intents through a venue's `IOrderGateway` (ADR 0016). Profit Fox Designer ADR 0055
defined Bybit Spot execution: signed V5 requests, a deterministic `orderLinkId`, lookup before any resubmission and
the rule that an ambiguous empty lookup is not proof that a submit never arrived. Bybit's realtime cache can lose
closed orders and its history lags, so a lost response cannot be resolved by placing again. ProfitGraph keeps that
behavior in `Venues.Bybit`; the private stream and reconciliation follow in stage 5.8.

## Decision

- **Trading switch.** Only a Worker with the `live` role registers the gateway and its write client
  (`AddBybitTrading`), and only when `Live:Trading:Enabled` is true. Every committed setting keeps it false. Without it a
  bot degrades with `NoGateway` and nothing is sent (ADR 0016).
- **Two clients.** `BybitReadClient` signs only GET requests to an allowlist (`/v5/account/fee-rate`,
  `/v5/order/realtime`, `/v5/order/history`) and reads public market data; every process may have it, because the API
  reads a credential's fee rates when a bot is created. `BybitTradeClient` signs only `/v5/order/create` and
  `/v5/order/cancel`. Both remove HTTP factory logging, so headers with the API key never reach a log.
- **Signing.** HMAC-SHA256 over timestamp, API key, a 5 000 ms receive window and the exact query or JSON body, as
  lowercase hex. The timestamp follows Bybit's public server time, measured at the midpoint of a request faster than a
  second and reused for five minutes; a timestamp rejection (`10002`) measures it again.
- **Client order ID.** `pg` + the bot GUID's 16 bytes in base64url (22 characters) + `-` + the engine order ID's 8
  big-endian bytes in base64url (11 characters): exactly 36 characters, deterministic and lossless. The prefix routes
  venue events to the bot. Profit Fox Designer's `pf` orders are foreign to ProfitGraph (stage 5 plan S5-6).
- **Orders.** Quantities and prices round down to the instrument's steps; an order below Bybit's minimums is rejected
  locally (`BelowMinNotional`) before anything is sent. A market buy with a quote budget is sized in quote
  (`marketUnit = quoteCoin`), every other market order in base; a market sell is valued at the public last price for
  the minimum check.
- **Outcomes.** Only explicit Bybit codes reject: insufficient balance (`110004`, `110007`, `110012`) as
  `InsufficientQuote` for a buy or `InsufficientBase` for a sell, price errors as `InvalidPrice`, `110017` as
  `ZeroQuantity`, other known codes as `InvalidAmount`. A timeout, an HTTP error, a rate limit or an unknown code is an
  exception, so the intent retries and never becomes a rejection.
- **Lookup.** A retry of a possibly placed submit, a duplicate response (`110072`) and a refused cancel look the order up
  by client ID in open, then in historical orders, and publish its state to the bot's runner as engine updates:
  acceptance, then the cumulative fill or the final state. An empty lookup throws and the submit is never placed
  again; a cancel is resolved only when the order is final. A buy's fee must be in the base asset and a sell's in the
  quote asset; any other fee asset throws `BybitUnsupportedFeeAssetException`, which degrades the bot in stage 5.8.
- **Fee rates.** `IVenueFeeRateReader` reads `GET /v5/account/fee-rate` for the bot's symbol: maker for limit, taker for
  market orders.

## Owner checks before trading

As Profit Fox Designer ADR 0055 requires, the owner verifies on the intended account, before enabling the switch:
how long Bybit remembers a duplicate `orderLinkId`; whether any response proves that an ambiguous submit was not
accepted; the key's Spot-trade and withdrawal rights and its IP restriction; and the fee assets of ordinary fills and
maker rebates. Agents never use a real key or call an authenticated endpoint; every test uses the mock exchange.

## Consequences

- The gateway is tested only against a stateful mock exchange that verifies signatures with an invented secret.

## Amendment: reconciliation and the private stream (stage 5.8)

- **Reconciliation** (ADR 0016) reads `GET /v5/user/query-api` first: a read-only key, a key without `SpotTrade` or
  with `Withdraw`, or a key Bybit refuses (`10003`, `10005`, `10010`) degrades the bot with `KeyPermissions`. It then
  opens the bot's private stream, looks up every order the bot asks about, and lists open orders and recent executions
  (`/v5/execution/list`, paged) on the symbol. Both reads are added to the read client's allowlist.
- **Private stream.** One authenticated connection to `wss://stream.bybit.com/v5/private` per running bot,
  subscribed to `order.spot` and `execution.spot`. The `auth` signature is HMAC-SHA256 of `GET/realtime` and an expiry
  10 s past Bybit's server time; an application ping goes every 20 s. The stream is open before the lookups, so no
  change between them is missed; its earlier updates are discarded because reconciliation reads the state again.
- **Messages.** A message naming one of the bot's orders on its symbol looks the order up and publishes its cumulative
  state, as a lookup does; Bybit's stream payload itself is not accounted. An order or execution on the symbol that is
  not the bot's ends the bot's updates with `ForeignOrder` unless the bot allows foreign orders. Other symbols are
  ignored.
- **Loss.** A closed connection, an unreadable message or a failed lookup ends the bot's updates with that error; the
  runner restarts and reconciles again. A connection per credential shared by its bots is left for later.
