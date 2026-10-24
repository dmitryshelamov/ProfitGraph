namespace ProfitGraph.Infrastructure.Jobs.Repositories.Contracts;

internal sealed class BackgroundJobDb
{
    public Guid Id { get; init; }
    public string Kind { get; init; } = string.Empty;
    public Guid OwnerId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Request { get; init; } = string.Empty;
    public string? Result { get; init; }
    public string? Failure { get; init; }
    public long Completed { get; init; }
    public long? Total { get; init; }
    public bool CancelRequested { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}