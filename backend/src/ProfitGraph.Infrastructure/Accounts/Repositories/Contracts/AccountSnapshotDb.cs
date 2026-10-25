namespace ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

internal sealed class AccountSnapshotDb
{
    public Guid Id { get; init; }
    public Guid CredentialId { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public DateTimeOffset BucketStart { get; init; }
    public decimal TotalValueUsd { get; init; }
    public decimal? AvailableValueUsd { get; init; }
    public string Coins { get; init; } = "[]";
}