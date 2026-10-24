# ADR 0007: Single Strategy Format

Status: Accepted, 2026-10-24.

## Context

The MVP kept compatibility code for an earlier graph format in the compiler, validator and parameter resolver,
and had two editor models (lanes, then flowchart blocks).

## Decision

- A strategy definition has one format: flowchart blocks, wires, named parameters and named values, as in
  Profit Fox Designer ADRs 0041 and 0042.
- Every stored definition carries `formatVersion`, starting at 1. A future format change ships an explicit upgrade
  of stored definitions instead of compatibility branches in the engine.

## Consequences

- Engine code contains no legacy branches.
- Strategies from Profit Fox Designer are not imported; templates are ported as code and fixtures.
