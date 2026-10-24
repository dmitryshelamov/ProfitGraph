# ADR 0011: Background Jobs

Status: Accepted, 2026-10-24.

## Context

History downloads and backtests run for minutes and must survive a closed browser. Profit Fox Designer ADR 0019
introduced a durable job queue that ran inside the API process and stopped running work through an in-process
registry. ProfitGraph runs jobs in the Worker (stage 3 plan, S3-11), so the API that receives a cancel request is
not the process that runs the job. Jobs also gain an owner, like strategies.

## Decision

- **Aggregate.** `BackgroundJob` owns the lifecycle and progress only, never the work's parameters or result:
  `Queued → Running → Completed | Failed | Cancelled` and `Queued → Cancelled`. Progress is `completed ≤ total`,
  monotonic within a stage; a new stage restarts it. Every transition is a method with Domain tests (ADR 0004).
  A job belongs to the user who started it; another user's job is reported as missing (`job_not_found`).
- **Requests.** A job request is a MediatR request implementing `IBackgroundJobRequest<TResult>`; the Worker sends it
  through the normal pipeline, so validation runs again at execution time. A request type is registered with a
  stable `BackgroundJobKind` (lowercase snake case), stored with the job; an unregistered kind or type is an error.
  Requests, results and failures are stored as JSONB.
- **Queue and execution.** The API only inserts queued jobs and reads them. The Worker polls the queue
  (`BackgroundJobs:PollingInterval`, default 1 s) and runs one job at a time in creation order. A short transaction
  claims the next job with `FOR UPDATE SKIP LOCKED` and marks it `Running`; the work runs outside any transaction in
  its own DI scope. Progress is written by separate conditional updates while the job is `Running`.
- **Cancellation across processes.** Cancelling a queued job is a conditional update to `Cancelled`. Cancelling a
  running job sets `cancel_requested`; the Worker checks the flag on every polling interval while the job runs and
  cancels the work's token. The Worker records `Cancelled` after the work stops. A second request, or a request for
  a finished job, is a conflict (`job_cancel_requested`, `job_finished`).
- **Failures.** Application and Domain exceptions become `failure { code, message }` with their stable codes.
  Anything else is logged and stored only as `unexpected_error`.
- **Restart.** On startup the Worker marks jobs left in `Running` as `Failed` with `background_job_interrupted`; a
  graceful shutdown records the same code for the job it interrupts. Work is not resumed; the next attempt starts
  over (downloads continue from the candle cache). This assumes one Worker process; leases arrive with the live
  runtime (stage 5).
- **Heartbeat.** The Worker records a heartbeat in `worker_heartbeats` on every polling pass. `/api/health/ready`
  reports `Degraded` when the heartbeat is older than three polling intervals, so a stopped Worker is visible
  without failing API readiness.
- **HTTP.** `GET /api/jobs/{id}` returns status, progress, result and failure; `POST /api/jobs/{id}/cancel`
  returns `202` with the job, `409` for a finished job or a repeated request and `404` for an unknown or foreign one.
  Starting a job is part of the feature that owns it, which answers `202 Accepted` with `Location: /api/jobs/{id}`.

## Consequences

- The Worker is required for jobs to run; the dev stack and end-to-end tests start it next to the API.
- Pause and resume (Profit Fox Designer ADR 0044) return with optimization in stage 4.
