# ADR 0008: Documentation and Change Workflow

Status: Accepted, 2026-10-24.

## Context

In the MVP, the architecture document grew to 1,766 lines mixing rules, status and implementation details, the
requirements document fell behind the product, and parallel agent branches produced a history that is hard to read.

## Decision

- `docs/architecture.md` contains binding rules only and stays short. Status and order of work live in
  `docs/roadmap.md`. Reasoning lives in ADRs.
- Documentation describes only what exists in this repository or is explicitly marked as planned.
- `main` changes only through pull requests with a green CI. Pull requests are squash-merged, so each one becomes
  a single Conventional Commit and the history stays linear.
- One pull request carries one feature or module together with its tests.

## Consequences

- The history reads as a list of features and fixes.
- Documentation updates are part of the pull request that changes behavior.
