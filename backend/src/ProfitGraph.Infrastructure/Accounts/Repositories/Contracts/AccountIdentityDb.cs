namespace ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

internal sealed class AccountIdentityDb
{
    public Guid CredentialId { get; init; }
    public string? AccountId { get; init; }
    public string? AccountName { get; init; }
    public string? AccountKind { get; init; }
    public string? AccessLevel { get; init; }
    public bool? ReadOnly { get; init; }
    public string[] Permissions { get; init; } = [];
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? ReadAt { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    public string? FailureReason { get; init; }
    public string? FailureDetail { get; init; }
    public DateTimeOffset? NextAttemptAt { get; init; }
}