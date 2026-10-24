# ADR 0001: Rebuild from Profit Fox Designer

Status: Accepted, 2026-10-24.

## Context

Profit Fox Designer reached a working MVP in four weeks: deterministic backtesting, optimization, and live bots on
Bybit, T-Invest and Steam. An architecture review of `mvp@f26ab20d` found that the engine and accounting core are
sound, while the live runtime and venue integrations accumulated structural problems: venues are strings that leak
into every layer, the live domain is anemic with state transitions inside SQL, the two application layers use
different styles, documentation drifted from the code, and the history is hard to read.

## Decision

ProfitGraph is a new repository with a new history. Code is **ported module by module** into the target
architecture rather than rewritten from scratch. Old tests and regression fixtures are the behavioral specification;
behavior changes only deliberately and is recorded in the pull request.

- The archive is the Profit Fox Designer repository at `mvp@f26ab20d`; it is not modified.
- Product decisions recorded in Profit Fox Designer ADRs remain in force unless a ProfitGraph ADR replaces them.
  When a module is ported, its still-relevant decisions are restated in a ProfitGraph ADR or document.
- Not carried over: legacy graph-format compatibility, the lane editor, Profit Fox import and handover, the shared
  workspace without users, and the MVP migrations.
- Production starts from an empty database. Exchange credentials are entered again by the owner.
- Venue order: Bybit first (wave 1), T-Invest second (wave 2), Steam later as a separate model (wave 3).

## Consequences

- Each wave delivers a working product end to end before the next venue is added.
- The old production keeps running until the owner switches over.
