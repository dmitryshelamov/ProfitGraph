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
    public Task<BackgroundJobInternal> Enqueue<TRequest>(UserId ownerId, TRequest request, CancellationToken cancellationToken)
        where TRequest : IBackgroundJobRequest
    {
        return Enqueue(BackgroundJobId.New(), ownerId, request, cancellationToken);
    }

    /// <summary>
    ///     Queues work under an identifier chosen beforehand, for work that a record with the same identifier describes.
    /// </summary>
    public async Task<BackgroundJobInternal> Enqueue<TRequest>(BackgroundJobId id, UserId ownerId, TRequest request, CancellationToken cancellationToken)
        where TRequest : IBackgroundJobRequest
    {
        BackgroundJob job = BackgroundJob.Queue(id, TRequest.Kind, ownerId, clock.GetUtcNow());
        await repository.Add(job, request, cancellationToken);

        return job.ToInternal();
    }
}