using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Optimization;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Infrastructure.Jobs.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Jobs.Repositories.Converters;

internal static class BackgroundJobDbConverter
{
    public static BackgroundJob ToDomain(this BackgroundJobDb row)
    {
        return BackgroundJob.Restore(
            new BackgroundJobId(row.Id),
            new BackgroundJobKind(row.Kind),
            new UserId(row.OwnerId),
            Enum.Parse<BackgroundJobStatus>(row.Status),
            new BackgroundJobProgress(row.Completed, row.Total),
            row.CancelRequested,
            row.CreatedAt,
            row.StartedAt,
            row.FinishedAt,
            row.UpdatedAt,
            row.PauseRequested,
            row.Interruptions,
            row.Advanced);
    }

    public static BackgroundJobDb ToDb(this BackgroundJob job, string request, string? result, string? failure)
    {
        return new BackgroundJobDb
        {
            Id = job.Id.Value,
            Kind = job.Kind.Value,
            OwnerId = job.OwnerId.Value,
            Status = job.Status.ToString(),
            Request = request,
            Result = result,
            Failure = failure,
            Completed = job.Progress.Completed,
            Total = job.Progress.Total,
            CancelRequested = job.CancelRequested,
            PauseRequested = job.PauseRequested,
            Interruptions = job.Interruptions,
            Advanced = job.Advanced,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            FinishedAt = job.FinishedAt,
            UpdatedAt = job.UpdatedAt
        };
    }

    public static ActiveJobInternal ToInternal(this ActiveJobDb row)
    {
        return new ActiveJobInternal(
            row.Id,
            row.Kind,
            row.Lane,
            Enum.Parse<BackgroundJobStatus>(row.Status),
            row.Completed,
            row.Total,
            row.CancelRequested,
            row.PauseRequested,
            row.CreatedAt.ToUniversalTime(),
            row.StartedAt?.ToUniversalTime(),
            row.QueuePosition is { } position ? (int)position : null,
            row.StrategyId,
            row.StrategyName,
            row.StrategyVersion,
            row.Venue,
            row.Symbol,
            row.Timeframe,
            row.Method is { } method ? OptimizationNames.Method(Enum.Parse<OptimizationSearchMethod>(method)) : null,
            row.OptimizationId);
    }
}