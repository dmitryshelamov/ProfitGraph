namespace ProfitGraph.Application.Jobs.Contracts;

/// <summary>
///     The user's active jobs, lane by lane with the running job first, and whether the Worker is beating its heartbeat:
///     without it queued jobs do not move.
/// </summary>
public sealed record ActiveJobsInternal(IReadOnlyList<ActiveJobInternal> Jobs, bool WorkerOnline);