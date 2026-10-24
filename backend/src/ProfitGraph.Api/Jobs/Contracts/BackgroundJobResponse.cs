using System.Text.Json;

namespace ProfitGraph.Api.Jobs.Contracts;

/// <summary>
///     A job's status and progress. The result's shape depends on the kind of work; the feature that starts the job
///     documents it.
/// </summary>
public sealed record BackgroundJobResponse(
    Guid Id,
    string Kind,
    string Status,
    long Completed,
    long? Total,
    bool CancelRequested,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    JsonElement? Result,
    BackgroundJobFailureResponse? Failure);