namespace ProfitGraph.Venues.Steam.Persistence.Repositories.Contracts;

internal sealed class SteamAssetReservationDb
{
    public string ClientOrderId { get; init; } = string.Empty;
    public Guid CredentialId { get; init; }
    public int AppId { get; init; }
    public string ContextId { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
    public DateTimeOffset UnlockAt { get; init; }
    public long SellerMinorUnits { get; init; }
    public string State { get; init; } = string.Empty;
    public string? ListingId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}