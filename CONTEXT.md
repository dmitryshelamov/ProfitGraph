# ProfitGraph Context

Domain glossary. Product terms (strategy, block, backtest run, order lifecycle, bot, and so on) are carried over
from the Profit Fox Designer glossary together with the modules that implement them, so this file never
describes behavior that does not exist in this repository yet.

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

### Venue Bot Settings

A typed, versioned settings object owned by a venue module for venue-specific bot configuration, such as trading
sessions. The bot aggregate stores it without interpreting its fields.

### Composition Root

A host project that wires the application together: `ProfitGraph.Api` for HTTP and `ProfitGraph.Worker` for the
live runtime. Only composition roots reference venue modules.
