namespace ProfitGraph.Venues.Steam.Persistence.Repositories.Contracts;

internal sealed class SteamMarketReceiptDb
{
    public long Position { get; init; }
    public string Receipt { get; init; } = string.Empty;
}