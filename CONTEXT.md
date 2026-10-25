# ProfitGraph Context

Domain glossary. Product terms are carried over from the Profit Fox Designer glossary together with the modules
that implement them, so this file never describes behavior that does not exist in this repository yet. Terms of
backtest runs, optimization, bots and accounts arrive with their stages.

## Terms

### Engine

The pure strategy computation core: lowering, graph validation and compilation, candle evaluation, causal dispatch,
trading accounting and replay. It performs no I/O and knows nothing about live trading or concrete trading venues.
Backtests and live bots execute strategies through the same engine. See [ADR 0002](docs/adr/0002-solution-projects-and-feature-folders.md).

### Trading Venue

A place where a strategy's orders are executed and its market data comes from, such as an exchange or a broker.
The core identifies a venue only through a `VenueId` value object; a venue has no default.

### Venue Module

A `ProfitGraph.Venues.*` project that implements the venue ports for one trading venue: historical and live candles,
instrument rules, fees, credential checks and order execution. Only venue modules may name concrete venues.
See [ADR 0003](docs/adr/0003-venue-modules.md).

### Venue Capability

A feature a venue module supports, expressed by the presence of the corresponding port, such as public history or
order execution. A missing capability is a missing port, never a flag with a default implementation.

### Venue Descriptor

What a venue module registers about itself: its `VenueId`, display name, supported timeframes and the rule that
turns typed input into the venue's instrument symbol (`btc/usdt` → `BTCUSDT` on Bybit). An unknown venue is an
error, never a default. See [ADR 0010](docs/adr/0010-venues-and-market-data.md).

### Candle Series

The stored history of one instrument of one venue on one timeframe. Stored candles are a cache of public market
data shared by all users; only the Owner can change it by CSV import, and downloads only add missing candles.

### History Download

A background job that brings the closed candles of a series into the shared cache from a venue's public history. It
fetches only the missing ranges, stores them chunk by chunk, and never changes a stored candle.

### Market Closure

A half-open range of a series' grid in which the venue's market was closed, so the series has no candle there, for
example the night between two trading days. A venue reports closures with its history; they are cached beside the
candles and never changed, and a CSV candle inside one is refused.

### Candle Without Trades

A candle of an open market in which nothing traded: flat at the last real close, with zero volume. Indicators and
candle-close work treat it as any candle, but a backtest fills no order on it; orders wait for a real candle. Only the
venue marks a candle so, and a CSV file cannot replace one.

### Instrument Rules Snapshot

The trading rules of one instrument (quantity step, price tick, minimums, asset precisions) as the venue reported
them at one moment. A snapshot is never updated and does not claim to be the rules that applied in the past; a run
uses the snapshot taken when it started.

### Prepared History

The immutable copy of candles one run replays: the warm-up before the run's start and the replay range, complete and
contiguous except where market closures cover a gap, with an identity that changes whenever any candle, closure or the
partition changes. Warm-up counts candles, so it reaches back over closures. A later import or download cannot change a
prepared history.

### Compilation Fingerprint

The identity of what a compiled strategy executes under the engine's execution policy. Editing labels, display names
or display units keeps it; changing any semantic value changes it.

### Backtest Run

One backtest of a strategy version with frozen parameters, period, settings and holdout. It is executed by a
background job with the same identifier and stores its ledger, one observation per replayed candle and its
diagnostics as immutable facts. See [ADR 0012](docs/adr/0012-backtest-runs.md).

### Chart Bucket

Consecutive replayed candles of a finished run collapsed into one chart point: open, high, low, close, volume, equity
range and fill counts. A run keeps at most 5000 buckets.

### Background Job

Durable work that runs in the Worker after the request that started it has returned, such as a history download
or a backtest: queued, running, then completed, failed or cancelled, with progress. A job belongs to the user who
started it; cancelling a running job is a request that the Worker honours. See
[ADR 0011](docs/adr/0011-background-jobs.md).

### Venue Bot Settings

A typed, versioned settings object owned by a venue module for venue-specific bot configuration, such as trading
sessions. The bot aggregate stores it without interpreting its fields.

### User

An invite-only account with a login that is unique ignoring case, a password hash, a role and an active or blocked
status. Only an active account can sign in; blocking ends its existing sessions. Strategies belong to the user who
created them; templates are shared.

### Owner

A user with authority to create and block accounts. An account cannot block itself, and the last active Owner cannot
be blocked. The first Owner is created from private startup configuration while no account exists.

### Exchange Credential

A user's secrets for one trading venue, such as an API key and secret, stored encrypted per value and never shown
again; only a four-character public hint is visible. For a venue whose credential selects one of several accounts, such
as a T-Invest brokerage account, the account is chosen from the venue's list when the credential is saved, and one venue
account has at most one credential. A credential belongs to its owner, and another user's credential is reported as
missing. See [ADR 0015](docs/adr/0015-exchange-credentials.md).

### Bot

A strategy running live on one venue series through one of its owner's exchange credentials. The owner sets its desired
state (running or stopped); the runtime that holds the bot's lease reports its runtime status. A degraded bot either
restarts automatically after a runtime failure or waits for the owner because of a safety reason. See
[ADR 0016](docs/adr/0016-live-bot-runtime.md).

### Bot Lease

The right of one runtime process to execute a bot, renewed while it works. Taking over an expired lease raises its
epoch, and every state write checks the epoch, so a stale process cannot commit. Its submit gate opens only after the
bot's venue state was reconciled.

### Order Intent

A submit or cancel for the venue, saved in the same transaction as the engine checkpoint that produced it and then
delivered by a dispatcher. A submit that may have reached the venue is looked up before any retry.

### Kill Switch

A per-bot control, or the global trading halt, that stops bots at once, cancels their open orders and rejects
submits that never left the process. It never sells a position.

### Composition Root

A host project that wires the application together: `ProfitGraph.Api` for HTTP and `ProfitGraph.Worker` for the
live runtime. Only composition roots reference venue modules.

### Two-SMA Crossover

A strategy using two simple moving averages with user-configurable periods: the fast average crossing above the slow average produces a buy signal, and crossing below produces a position-closing signal.

### Ladder

A strategy from the owner's Profit Fox bot: a market entry starts a cycle, limit buys average into the position as the price falls, and a limit sell closes it above its average price. In the designer, it is provided as an editable definition of event, condition, order and cancellation blocks.

### Trading Strategy

A user-authored strategy definition containing blocks, wires, named parameters and named values. This is the model
edited on the canvas, returned by strategy/template APIs, and stored in immutable strategy versions. Layout is
stored alongside the definition and never affects execution. See Profit Fox Designer ADR 0042.

A strategy belongs to the user who created it: other users neither see it in their list nor open, save or validate
it, and the API answers them as if the strategy did not exist. Saving creates the next version and requires the
version the editor started from, so a concurrent save is rejected instead of overwritten.

### Block

A visible element of a strategy definition with a stable ID, an optional display name and its settings. The four
kinds are event, condition, order and cancel. Settings hold value expressions directly; the editor does not convert
blocks into primitive nodes.

### Check

One item in a condition block's ordered list of checks. A condition joins its checks using all or any and sends an
incoming event to its yes or no exit. Checks include comparisons, crosses and the supported built-in position,
active-order, balance and order-size checks. A check is a setting of its block, rather than a separate canvas node.

### Value

A named, reusable value expression, such as a moving average or a calculated next order price. Expressions can
reference candle fields, indicators, balances, positions, active orders, constants, parameters and other named
values, and combine numeric operands with arithmetic. Editing a shared value updates all its references without
creating extra canvas blocks.

### Parameter

A named numeric setting with a declared value type and an exact default value. Blocks and values refer to a
parameter by name. Backtest overrides and optimization search-space ranges also use that name, rather than an
internal node ID or parameter-port key. Ranges belong to an optimization request, rather than a saved definition.

### Wire

An event connection from a block's named exit to another block: `{ from: { block, exit }, to }`. Several wires may
enter the single event input of a condition, order or cancel block. The definition represents this merge directly, without a user-authored merge
block. Wires can express causal dependencies such as cancellation completion before replacement submission and
order-event cycles; they do not introduce unrestricted loops.

### Event Block

A visible start source such as OnStart, OnCandleClosed or an order lifecycle listener. Its out exit starts a path
through the strategy's other blocks. Order blocks can also continue a path directly through their submitted,
filled, cancelled and rejected exits.

### Lowering

The deterministic server-side conversion of a block definition into the internal StrategyGraph before graph
validation and compilation. It creates primitive value, Boolean, Decision and MergeEvents nodes as needed, with a
source map back to blocks, named values and parameters. Public strategy-validation diagnostics identify these
sources and never expose generated node IDs. The existing compiler, execution engine and replay consume the
lowered graph without changes to their execution rules.

### Node

An internal element of StrategyGraph that receives, computes or consumes data and events. Primitive nodes belong
to lowering, validation, compilation and frozen execution snapshots; they are not the strategy-authoring model.
The catalog documentation describes the user-facing model and lists internal primitives separately.

### Connection

A typed link between compatible ports in the internal StrategyGraph. Value connections carry calculations and
event connections carry occurrences. The user-authored equivalent of an event connection is a wire; expressions
represent user-authored value dependencies.

### Strategy Template

A ready-made, editable block definition available to the user. SMA has five visible blocks and Ladder has thirteen.
The user can edit blocks, checks, wires, parameters and shared values directly. Layout starts without saved
positions; the editor supplies a grid layout and preserves explicitly saved positions.

### Closed Candle

A candle whose time interval has ended. Its completion triggers the associated candle-event blocks in the engine; the execution timing of resulting trading signals is defined separately.

### Long-Only Spot Strategy

A spot-market strategy that can buy an asset and subsequently sell the acquired quantity, but does not open short positions or use borrowed assets.

### Backtest

Replaying a strategy's decisions on historical market data to evaluate its behavior and results. A backtest never places real orders.

### Total PnL

Ending cash plus the remaining position's value at the last processed candle's close, minus starting capital, with incurred execution fees already accounted for. The remaining position is not forcibly sold; realized and unrealized results are shown separately.

### Terminal Position Valuation

Valuing the remaining position at the last processed candle's close when a run ends. This valuation does not create a sale or charge hypothetical future exit fees or slippage.

### OHLC Execution Model

A backtest model that simulates each candle using the fixed price path Open → Low → High → Close. Orders are processed sequentially along this assumed path; a newly created order may use only subsequent price movement, never an earlier part of the candle.

### Full Limit Fill

The engine assumption that a limit order fills its entire remaining quantity when the simulation considers it executable. Partial fills and order-book liquidity constraints are not modeled for limit orders.

### Market Order Slippage

A user-configurable percentage set before a backtest run that adjusts market-order execution prices adversely: buys use reference price × (1 + s), and sells use reference price × (1 - s), where s is the percentage expressed as a fraction. For market orders generated by a closed-candle signal, the reference is the next candle's open.

### Next-Open Execution

Execution of a market order generated by a closed-candle signal at the next candle's open, adjusted for market-order slippage. If no next candle exists within the run's date range, that order remains unexecuted.

### Execution Fee

A cost withheld from the asset received in a spot execution: base asset on buys and quote currency on sells. The user configures separate nonnegative limit-order and market-order fee rates; these are simulation categories, not guaranteed exchange maker/taker classifications.

### Buy Amount Input

The buy order block's amount expression specifying quote currency to spend on a purchase, supplied by a constant or a calculation such as a percentage of available balance. No additional quote-currency fee is charged: the purchase fee reduces the received base quantity.

### Order Size Mode

An authoring choice of quote amount or base quantity, independent of order side. Inverse modes convert at the signal's
explicit positive price into the existing execution input: Buy quantity becomes quote budget, Sell amount becomes base
quantity. The actual executed size may differ with price normalization, next-open price movement and fees. Order-size
preview checks support the same choice. Nonpositive runtime conversion prices suppress submission without changing
engine execution rules; candle prices are unavailable at a start before any closed candle exists.

### Starting Capital

The user-configured initial cash balance in the selected trading pair's quote currency, set before each backtest run. The initial base-asset balance is zero; starting with an existing asset holding is not supported.

### Available Balance

The asset balance not reserved by active orders. Buy reservations consume available quote currency; sell reservations consume available base quantity, while reserved assets remain part of total equity.

### Out-of-Sample Holdout

The last part of a run's replay candles, by default 20% by bar count, reported separately from the in-sample part. Optimization and walk-forward never read it; only the chosen parameters are checked on it.

### Action Completion Event

A result occurrence emitted after an action commits its state change, allowing downstream blocks to observe updated balances and orders. Submission completion means accepted, not filled; cancellation completion means its reservation has been released or no active target existed.

### Order Tag

A strategy-assigned semantic name identifying an order's purpose, such as `ladder.exit`. Under the execution contract, at most one active order can occupy a tag; a later terminal replacement gets a new order ID while reusing that tag.

### Manual Order

An owner-submitted market order in a running live bot, outside the strategy graph (Profit Fox Designer ADR 0091).
It enters the bot's engine account with the reserved tag `manual` and no originating block, so balances, risk limits and
the position ledger include it, while the graph observes it only through global listeners filtered by that tag.

### Order Lifecycle Output

An exit on an order block reporting events for orders it created: submitted, filled, cancelled or rejected. The event remains associated with its originating block even when another block cancels the order; global listeners observe the same underlying occurrence.

### Pending Submit

An externally executed order accepted locally with its funds reserved and tag occupied, while exchange acceptance
is still outstanding. The graph sees it as active; Submitted is emitted only after confirmation.

### Pending Cancel

An active externally executed order whose cancellation has been requested but not confirmed. Its remaining
reservation and tag stay active. A final fill may win the race before cancellation confirms.

### External Order Update

One accepted, rejected, cumulative fill or cancellation-confirmation update supplied to shared graph dispatch by
an external account. It starts a fresh causal root against the last closed candle; it does not place an order.

### Engine Checkpoint

A plain-data snapshot of an externally executed graph engine between causal roots: account balances and open
orders, cumulative fill progress, pending dispatcher correlations and counters, the last closed candle, started
state and graph-version ownership. Indicators are reconstructed from closed-candle history on restore.

### Resume

A graph event emitted once after an engine restart or a graph-version change, on the last closed candle at the
external phase. It carries the reason and previous/current graph version IDs. It lets an `OnResume` node react
without running `OnStart` again.

### Action Evaluation Mode

Internal execution metadata selecting when a lowered action node evaluates. User-authored order and cancellation blocks receive explicit event wires and lower to OnEvent actions. Candle-close processing is represented by an OnCandleClosed event block, with the existing next-open execution rule.
