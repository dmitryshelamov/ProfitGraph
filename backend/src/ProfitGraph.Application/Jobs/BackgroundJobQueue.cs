using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Jobs.Converters;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Jobs;

/// <summary>
///     Queues work for the Worker on behalf of a user. Commands that start jobs call it inside their transaction.
/// </summary>
public sealed class BackgroundJobQueue(IBackgroundJobRepository repository, TimeProvider clock)
{
    public async Task<BackgroundJobInternal> Enqueue<TRequest>(UserId ownerId, TRequest request, CancellationToken cancellationToken)
        where TRequest : IBackgroundJobRequest
    {
        BackgroundJob job = BackgroundJob.Queue(BackgroundJobId.New(), TRequest.Kind, ownerId, clock.GetUtcNow());
        await repository.Add(job, request, cancellationToken);

        return job.ToInternal();
    }
}