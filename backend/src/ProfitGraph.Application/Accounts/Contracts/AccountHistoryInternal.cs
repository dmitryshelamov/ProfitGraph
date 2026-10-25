namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     A credential's account value history, oldest first, with how reading it goes.
/// </summary>
public sealed record AccountHistoryInternal(
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    string? FailureReason,
    DateTimeOffset? NextAttemptAt,
    IReadOnlyList<AccountSnapshotInternal> Snapshots);