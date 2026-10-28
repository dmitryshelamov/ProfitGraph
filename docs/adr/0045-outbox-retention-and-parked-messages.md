# ADR 0045: Outbox Retention and Parked Messages

Status: Proposed, 2026-10-27. Amends the outbox of ADR 0016 (stage 5.3). The owner reviews it in the pull request.

## Context

The outbox (ADR 0004, ADR 0016) stores every Domain event in `outbox_messages` with the commit that raised it, and the
Worker's dispatcher delivers it once. A review of `main` found two gaps:

- Delivered rows were never deleted, so the table grew with every event a bot raised, without bound.
- A message whose handler kept failing (a bug, a constraint the handler violates, a type this build does not know) was
  retried forever with backoff capped at five minutes. Nothing told anyone: its effects (feed entry, alert, Telegram
  message) were silently missing.

## Decision

- **Retention.** The Worker's `live` role, which delivers the outbox, runs `OutboxRetentionHost`: at start and then every
  hour it sends `PruneOutboxCommand` until a batch deletes fewer rows than its size. Each command deletes at most
  `Outbox:RetentionBatchSize` (default 1000) messages delivered more than `Outbox:Retention` (default 14 days) ago, in
  its own transaction, so no long transaction or lock builds up. Undelivered and parked messages are never deleted.
  The use case follows `PruneBotFeedCommand` (ADR 0021): the command and its handler in
  `Application/Common/Outbox/PruneOutbox`, the port `IOutboxStore`, the SQL in `OutboxQueries`.
- **No index on `delivered_at`.** The delete selects by `delivered_at < cutoff` with a limit and no order. Measured on
  PostgreSQL 16 with 750,000 delivered rows (255 MB, 15 days at 50,000 events a day): a batch of 1000 takes about 6 ms,
  because the oldest rows come first in the heap, and the final pass that finds nothing scans the table in about 100 ms.
  Once an hour this is negligible, while an index would add a write to every delivery, the outbox's hot path. The
  table now stays bounded by the retention, so the scan stays bounded too.
- **Parking.** A failed delivery still waits with backoff (1, 2, 4, ... seconds up to `Outbox:MaxRetryDelay`). The
  failure that brings `attempts` to `Outbox:MaxAttempts` (default 10: with the default delays the tenth failure comes about
  nine minutes after the first) also sets `parked_at`, keeping `last_error` (the exception type or `unknown_event_type`). The dispatcher no
  longer claims a parked message. Migration `202610280020` adds the column, a check that a parked message is
  undelivered, and a partial index on parked messages. The same `UPDATE` counts the failure and parks, so the limit
  applies to the stored count whichever dispatcher failed.
- **Unknown types park too.** ADR 0016 kept a message of an unknown type and retried it. It now parks after the same
  number of attempts: a Worker that lags behind the API by more than about nine minutes is an incident worth seeing, and
  the message is still kept.
- **Signal.** Parking logs one warning with the message ID, its type, the attempt count and the last error, never the
  payload. `/api/health/ready` gets an `outbox` check: Degraded while any message is parked, with the count in the
  description. Readiness stays 200, as for a stale Worker heartbeat (ADR 0011), because the API itself keeps serving.
- **Release.** After the cause is fixed and deployed, the operator releases parked messages, which are then delivered
  in their stored order and take effect once:

  ```sql
  UPDATE outbox_messages SET parked_at = NULL, attempts = 0, next_attempt_at = NULL WHERE parked_at IS NOT NULL;
  ```

- **Options.** `Outbox:MaxAttempts`, `Outbox:Retention` and `Outbox:RetentionBatchSize` join `Outbox:PollingInterval` and
  `Outbox:MaxRetryDelay`; every one must be positive, or the process does not start.

## Alternatives

- **An alert row per parked message.** Alerts (`bot_alerts`) belong to a bot and are raised by Domain events of its
  aggregates. An outbox message is infrastructure and may not belong to a bot; an alert about the outbox would need its
  own owner, kind and texts, or would itself go through the outbox that just failed.
- **A Telegram message.** Telegram has its own delivery queue fed by a handler, which is the mechanism that may be
  failing. Readiness is read without the outbox.
- **A separate dead-letter table.** Moving a message out would need a second copy of its row and a way back. A column
  keeps the message where its sequence already orders it, and release is one `UPDATE`.

The readiness check is the smallest signal that fits: readiness already reports a stopped Worker and live runtime the
same way, it is the endpoint monitoring reads, and it costs one indexed count. The web UI reads only liveness today, so
the owner sees a parked message through readiness or the Worker log, not on a page.

## Consequences

- `outbox_messages` holds at most the retention's worth of delivered events plus the undelivered and parked ones.
- A delivered event can no longer be replayed from the outbox after 14 days; nothing reads delivered events.
- An effect that depends on a parked message, such as a feed entry or an alert, stays missing until the operator
  releases it; readiness says so meanwhile. Events after it are still delivered: the outbox never guaranteed order
  across a failed message.
- Readiness may report Degraded for a reason that needs an operator, not only for a stopped Worker.
