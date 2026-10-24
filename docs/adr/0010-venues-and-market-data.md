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

