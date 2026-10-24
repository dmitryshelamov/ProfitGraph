using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Jobs;

/// <summary>
///     Loads a job for its owner. A job of another user is reported as missing, so its existence is not revealed.
/// </summary>
internal static class OwnedJobs
{
    internal static async Task<BackgroundJob> GetOwned(this IBackgroundJobRepository repository, Guid id, Guid userId, CancellationToken cancellationToken)
    {
        BackgroundJob? job = id == Guid.Empty ? null : await repository.Get(new BackgroundJobId(id), cancellationToken);

        return job is not null && job.IsOwnedBy(new UserId(userId))
            ? job
            : throw new ResourceNotFoundException("The background job does not exist.", BackgroundJobErrorCodes.NotFound);
    }
}