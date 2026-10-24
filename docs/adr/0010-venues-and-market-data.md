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
