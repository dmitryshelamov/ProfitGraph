namespace ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

internal sealed class AccountBalanceStatusDb
{
    public Guid CredentialId { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public string? FailureReason { get; init; }
    public string? FailureDetail { get; init; }
    public int ConsecutiveFailures { get; init; }
    public DateTimeOffset? NextAttemptAt { get; init; }
}