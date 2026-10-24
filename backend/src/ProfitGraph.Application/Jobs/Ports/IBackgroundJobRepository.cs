using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Jobs.Ports;

/// <summary>
///     Stores jobs with their request, result and failure. Updates apply only while the stored status still equals the
///     expected one, so the API and the Worker cannot overwrite each other's transitions.
/// </summary>
public interface IBackgroundJobRepository
{
    Task Add<TRequest>(BackgroundJob job, TRequest request, CancellationToken cancellationToken)
        where TRequest : IBackgroundJobRequest;

    Task<BackgroundJob?> Get(BackgroundJobId id, CancellationToken cancellationToken);

    /// <summary>
    ///     Reads the job with its stored result and failure.
    /// </summary>
    Task<BackgroundJobInternal?> GetDetails(BackgroundJobId id, CancellationToken cancellationToken);

    Task<bool> Update(
        BackgroundJob job,
        BackgroundJobStatus expectedStatus,
        IBackgroundJobResult? result,
        BackgroundJobFailureInternal? failure,
        CancellationToken cancellationToken);
}