# ADR 0005: MediatR Application Style

Status: Accepted, 2026-10-24.

## Context

The MVP used MediatR command and query handlers in the designer application layer and plain services in the live
application layer. MediatR 13 and later require a commercial license; 12.5.0 is the last Apache-2.0 release.

## Decision

- All use cases, in the API and in the Worker, are MediatR commands and queries with handlers.
- MediatR stays pinned to 12.5.0 and `MediatR.Contracts` to 2.0.1. Upgrading to a commercially licensed version
  requires a separate decision.
- The pipeline is built from MediatR behaviors: validation, then a database transaction for commands not marked
  `INonTransactionalCommand`.
- Domain events implement `INotification` through `MediatR.Contracts`; Engine has no MediatR dependency.

## Consequences

- Worker loops call commands such as processing a closed candle or dispatching a pending intent.
- Security fixes in later MediatR versions must be evaluated against the pinned version.
