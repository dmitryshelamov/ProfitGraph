namespace ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

internal sealed class AccountSnapshotDb
{
    public Guid Id { get; init; }
    public Guid CredentialId { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public DateTimeOffset BucketStart { get; init; }
    public decimal TotalValue { get; init; }
    public string ValueCurrency { get; init; } = string.Empty;
    public decimal? AvailableValue { get; init; }
    public string Coins { get; init; } = "[]";
}