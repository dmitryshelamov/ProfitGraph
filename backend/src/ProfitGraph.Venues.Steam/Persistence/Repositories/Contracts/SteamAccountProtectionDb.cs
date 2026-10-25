namespace ProfitGraph.Venues.Steam.Persistence.Repositories.Contracts;

internal sealed class SteamAccountProtectionDb
{
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset OpenedAt { get; init; }
}