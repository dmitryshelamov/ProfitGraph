namespace ProfitGraph.Application.Accounts.Contracts;

public sealed record AccountSnapshotInternal(
    DateTimeOffset CapturedAt,
    decimal TotalValue,
    string ValueCurrency,
    decimal? AvailableValue,
    IReadOnlyList<AccountCoinBalanceInternal> Coins);