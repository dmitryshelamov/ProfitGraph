namespace ProfitGraph.Api.Accounts.Contracts;

/// <summary>
///     A credential's account value history, oldest first, with how reading it goes; decimals are exact strings.
/// </summary>
public sealed record AccountHistoryResponse(
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    string? FailureReason,
    DateTimeOffset? NextAttemptAt,
    IReadOnlyList<AccountSnapshotResponse> Snapshots);