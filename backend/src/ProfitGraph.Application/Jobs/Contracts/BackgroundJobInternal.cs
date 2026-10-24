using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Jobs.Contracts;

public sealed record BackgroundJobInternal(
    Guid Id,
    string Kind,
    BackgroundJobStatus Status,
    long Completed,
    long? Total,
    bool CancelRequested,
    bool PauseRequested,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IBackgroundJobResult? Result,
    BackgroundJobFailureInternal? Failure);