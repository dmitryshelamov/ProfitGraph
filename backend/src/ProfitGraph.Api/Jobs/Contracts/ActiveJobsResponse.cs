namespace ProfitGraph.Api.Jobs.Contracts;

/// <summary>
///     The user's active jobs, lane by lane with the running job first; without an online Worker queued jobs do not move.
/// </summary>
public sealed record ActiveJobsResponse(IReadOnlyList<ActiveJobResponse> Jobs, bool WorkerOnline);