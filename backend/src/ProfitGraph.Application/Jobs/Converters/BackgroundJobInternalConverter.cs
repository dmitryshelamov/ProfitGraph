using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Jobs.Converters;

public static class BackgroundJobInternalConverter
{
    public static BackgroundJobInternal ToInternal(this BackgroundJob job, IBackgroundJobResult? result = null, BackgroundJobFailureInternal? failure = null)
    {
        return new BackgroundJobInternal(
            job.Id.Value,
            job.Kind.Value,
            job.Status,
            job.Progress.Completed,
            job.Progress.Total,
            job.CancelRequested,
            job.PauseRequested,
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt,
            result,
            failure);
    }
}