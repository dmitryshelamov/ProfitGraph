using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Jobs.Contracts;

namespace ProfitGraph.Application.Jobs.GetActiveJobs;

/// <summary>
///     The user's queued, running and paused jobs of every kind.
/// </summary>
public sealed record GetActiveJobsQuery(Guid UserId) : IQuery<ActiveJobsInternal>;