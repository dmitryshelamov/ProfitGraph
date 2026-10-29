using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Jobs.Ports;

/// <summary>
///     Reads the owner's queued, running and paused jobs with their places in the Worker's lanes.
/// </summary>
public interface IActiveJobReader
{
    Task<ActiveJobsInternal> ForOwner(UserId ownerId, CancellationToken cancellationToken);
}