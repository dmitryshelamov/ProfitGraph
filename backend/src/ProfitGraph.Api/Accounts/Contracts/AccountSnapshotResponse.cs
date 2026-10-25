namespace ProfitGraph.Api.Accounts.Contracts;

public sealed record AccountSnapshotResponse(DateTimeOffset CapturedAt, string TotalValueUsd, string? AvailableValueUsd, IReadOnlyList<AccountCoinBalanceResponse> Coins);