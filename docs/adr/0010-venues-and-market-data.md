# ADR 0010: Venues and Market Data

Status: Accepted, 2026-10-24.

## Context

ADR 0003 makes every trading venue a module behind Application ports. Stage 3 needs the first of them: venue
identity, a candle store and CSV import, followed by public Bybit history (Profit Fox Designer ADRs 0017 and 0021).
In the MVP a venue was a string with a silent Bybit default, symbol rules lived in a shared static class that named
every venue, and CSV import could be called by anyone sharing the workspace.

## Decision

- **Venue identity.** `VenueId` is a Domain value object: lowercase ASCII letters, digits and hyphens, 1–32
  characters, no default. A venue module registers an `IVenueDescriptor` with its identifier, display name,
  supported timeframes and symbol rule. `VenueRegistry` in Application resolves descriptors; an unknown venue is a
  validation error (`unknown_venue`), never a fallback.
- **Instrument symbols.** `InstrumentSymbol` is a Domain value object with only venue-neutral limits (1–64
  characters, no whitespace or control characters). The venue descriptor turns user input into a symbol: Bybit
  trims, upper-cases and drops `/` (`btc/usdt` → `BTCUSDT`). The core never parses a symbol itself.
- **Candle series.** `CandleSeries(VenueId, InstrumentSymbol, Timeframe)` names one history. The `candles` table is
  keyed by `(venue, symbol, timeframe, open_time)` and is a cache of public market data shared by all users.
  Candles are stored exactly as `numeric` with positive, consistent OHLC and nonnegative volume; no candle is ever
  synthesized.
- **Writes.** CSV import replaces stored candles only where values differ and records the source `csv`; the whole
  import is one transaction, so an invalid file stores nothing. Downloads (stage 3.3) append missing candles only
  and never overwrite (`ON CONFLICT DO NOTHING`). Prepared backtest history is an immutable copy (Profit Fox
  Designer ADR 0021), so a later import cannot change a run.
- **CSV import** is available to the Owner only (stage 3 plan, S3-10). The file is streamed, never buffered whole:
  UTF-8, header `openTime,open,high,low,close,volume` in any order, ISO-8601 UTC times with `Z` or `+00:00`, exact
  decimals with a dot. Rows are validated against the timeframe grid and must be complete candles (close time not
  in the future); identical duplicates are accepted once, conflicting duplicates are errors. Limits on rows, file
  size, request size and row length come from `CsvImport` configuration and are validated at startup. Error
  responses list at most `MaxDetailedErrors` rows plus a summary.
- **Reads.** `GET /api/historical-data/candles` returns at most 10 000 candles of a half-open UTC range, oldest
  first, with decimal strings. `GET /api/market-data/venues` lists registered venues.

## Consequences

- `ProfitGraph.Venues.Bybit` exists from stage 3.1 with its descriptor only; its data ports arrive in 3.3.
- Adding a venue adds a descriptor and a symbol rule in its module; the core, the API and the database schema do not
  change.
- A user who is not the Owner can read candles but not import them.
- A composition root (API, Worker) names a venue module only to register it (`using` and `Add...Venue()`); the
  venue-neutrality architecture test allows exactly these lines there and nowhere else.

## Amendment: history download (stage 3.3)

- A venue offers public history by registering an `IHistoricalCandleSource` (ADR 0003: a capability is a port).
  Downloading from a venue without one is a validation error (`historical_data_unavailable`).
- `POST /api/historical-data/downloads` queues a `historical_download` job (ADR 0011) for any signed-in user, because
  a download only adds missing candles. The start must come before the latest closed candle, which is checked when
  the job is queued and again when the Worker runs it.
- The Worker reads the clock once. Omitted bounds mean the venue's first candle, found by binary search over "is
  there a candle before t", and the latest closed candle; a later end is clamped to it. Only missing ranges are
  fetched, in chunks of `HistoricalDownload:MaxCandlesPerChunk` (10 000). Each chunk is checked completely (grid,
  OHLCV, range, duplicates, gaps) before it is stored in its own short transaction, so a failure keeps the chunks
  already stored and a later download continues from the cache.
- Bybit (stage 3 plan, S3-8) uses CryptoClients.Net/Bybit.Net 5.9.0 with only public requests. A handler checks the
  raw JSON before the SDK, so no price or volume is rounded silently. Retries (Polly), waiting until Bybit's
  rate-limit reset, per-attempt and total timeouts and a circuit breaker are configured in `Venues:Bybit`; resets
  use the system clock, never the application's. The MVP's general request limiter is not ported: for public
  history the SDK's own waiting and the reset times are enough; per-account limits return with orders (stage 5).

## Amendment: instruments and history preparation (stage 3.4)

- **Venue ports.** Every venue port names its venue (`IVenuePort`), and `VenuePorts<TPort>` finds the one registered
  for a venue. Besides history, a venue may offer `IInstrumentCatalog` (tradable pairs for picking one) and
  `IInstrumentRulesSource` (current trading rules of one instrument).
- **Pairs.** `GET /api/market-data/instruments?venue&query&limit` (limit 1–100, default 20) searches a venue's pairs
  by symbol or asset. A venue without a catalog is a validation error (`instrument_rules_invalid_request`). Bybit reads
  all trading Spot pairs with one public request and reuses the list for `Venues:Bybit:InstrumentListLifetime`
  (default one hour); a failed refresh keeps the previous list. Venue failures answer `502`
  (`instrument_rules_invalid_provider_data`) or `503` (`instrument_rules_provider_unavailable`).
- **Rules snapshots** (Profit Fox Designer ADR 0021). Capturing rules reads them from the venue outside any
  transaction and inserts an immutable row into `instrument_rules`, which is never updated. A snapshot records the
  rules as the venue reported them at capture time, not the rules that applied in the past. Bybit maps the base
  precision to the quantity step, the tick size to the price tick and the minimum order value to the minimum
  notional; precision increments must be exact powers of ten, and a guard rejects inexact decimals before the SDK.
- **Prepared history.** A run's history is resolved against one clock reading like a download, extended by as much
  warm-up as exists (up to the requested count, at most 100 000): a warm-up complete in the cache needs no venue,
  otherwise it is clipped to the venue's first candle. The whole range is brought into the cache and copied out,
  checked to be complete and contiguous. Missing candles are fetched only when the cache lacks them, so a venue
  without downloads can be prepared from imported history.
- **Identities.** The history identity is SHA-256 over length-prefixed UTF-8 parts (version `profitgraph-history/v1`,
  venue, symbol, timeframe, partition bounds as UTC ticks, warm-up count, every candle as exact decimal text). The
  compilation fingerprint is SHA-256 of a canonical JSON description of the compiled graph under the engine's
  execution policy; labels, display names and display units are left out, and decimals are exact text. Neither
  reproduces the MVP's values: ProfitGraph has no runs from the MVP to compare against.

## Amendment: instrument names in words (ADR 0022)

- `InstrumentSymbol` allows up to 256 characters with single inner spaces, so that a venue can name instruments in
  words (a Steam item is `KZT:730:Dreams & Nightmares Case`). Leading, trailing and repeated spaces, any other
  whitespace and control characters stay invalid. Every `symbol` column is `varchar(256)`. Bybit's rule is unchanged.

## Amendment: market closures (ADR 0023, TC1)

- `IHistoricalCandleSource.Get` returns the candles of a range with the ranges of it in which the market was closed
  (`HistoricalCandlesInternal`). Bybit and Steam report none. A closure must be on the grid, is clipped to the request
  and merged, and must not contain one of the venue's candles (`invalid_provider_data`).
- A download fetches only intervals that neither a candle nor a closure covers, and stores the chunk's closures in
  `candle_closures` in the same transaction as its candles; like candles they are appended and never changed. A gap that
  no closure covers still fails the chunk. A CSV row inside a stored closure is refused.
- Prepared history requires every grid interval to be a candle or to lie in a closure, carries the merged closures and
  hands them to replay, which accepts exactly the gaps they cover. Warm-up counts candles: when closures leave it short,
  preparation reaches back again, at most 16 times and each time at least doubling the span, until it has the requested
  candles or starts at the venue's first candle. The history identity appends the closures after the candles under a
  `closures` marker only when there are any, so an identity without closures keeps its bytes.
- Live bots still need contiguous history; they refuse closures until live closures arrive (ADR 0023, TC3).

## Amendment: candles without trades (ADR 0023, TC2)

- A venue may mark a candle as having no trades (`Candle.NoTrades`): open, high, low and close equal and volume zero,
  which the validator and a `candles` check constraint require. The cache stores the flag in `candles.no_trades`; existing
  rows are real candles. A CSV row neither creates such a candle nor replaces a stored one; replacing one is a later,
  explicit correction (ADR 0023, live candles).
- The history identity adds a `no-trades` marker after a flagged candle only, so identities of histories without such
  candles keep their bytes.

## Amendment: market data with the caller's credential (ADR 0023, TC6)

- A venue without anonymous market data reads history, instrument search and rules with the caller's own credential of
  that venue. Downloads (`POST /api/historical-data/downloads`), backtests and optimizations (`credentialId` in the
  backtest request) and the instrument search (`credentialId` query parameter) may name one; the start checks that it is
  the user's and of the series' venue (`404 exchange_credential_not_found`, `400 credential_venue_mismatch`).
- The job request (`DownloadCandlesCommand`, `RunBacktestCommand`, `RunOptimizationCommand`, and the finalists an
  optimization starts) keeps the credential's identifier; the frozen configuration, the compilation fingerprint and the
  history identity never contain it. The Worker decrypts it only around the use case's venue reads
  (`MarketDataAccess.Use`, which flows through that use case's awaited calls and nowhere else). A credential deleted
  since the job was queued gives no access, so a run whose history is complete in the cache still runs.
- Venue ports keep their signatures: a module that needs access reads `MarketDataAccess.Current`; Bybit and Steam ignore
  it. Bot creation reads the rules with the bot's credential; live reads follow with live closures (TC3).
