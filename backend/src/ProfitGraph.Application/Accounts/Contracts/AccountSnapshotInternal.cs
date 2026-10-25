namespace ProfitGraph.Application.Accounts.Contracts;

public sealed record AccountSnapshotInternal(
    DateTimeOffset CapturedAt,
    decimal TotalValueUsd,
    decimal? AvailableValueUsd,
    IReadOnlyList<AccountCoinBalanceInternal> Coins);