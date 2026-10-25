namespace ProfitGraph.Api.Accounts.Contracts;

public sealed record AccountSnapshotResponse(
    DateTimeOffset CapturedAt,
    string TotalValue,
    string ValueCurrency,
    string? AvailableValue,
    IReadOnlyList<AccountCoinBalanceResponse> Coins);