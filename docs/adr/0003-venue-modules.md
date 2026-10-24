# ADR 0003: Venue Modules

Status: Accepted, 2026-10-24.

## Context

In the MVP, a venue was a string constant where an empty value silently meant Bybit. Steam was mentioned in
116 files outside its own folders, including the replay engine, and the bot aggregate carried venue-specific fields.
The order gateway port exposed capability flags with default implementations, so venue behavior leaked into the
shared runtime and every new venue touched every layer.

## Decision

- `VenueId` is a value object without a default value.
- Each venue is a `ProfitGraph.Venues.*` module that registers a venue descriptor and implements venue ports
  defined in Application, for example historical candles, instrument rules, live candles, fee model, credential
  check and order gateway.
- A capability is expressed by the presence of a port. There are no capability flags with default behavior.
- Venue-specific bot configuration is a typed, versioned settings object owned by the module; the bot aggregate
  stores it without interpreting its fields.
- Only venue modules name concrete venues; an architecture test rejects venue names in other projects.

## Consequences

- Adding a venue adds a project and its registration; the core does not change. T-Invest (wave 2) validates this.
- Venue-specific failures are translated inside the module into application-level errors.
