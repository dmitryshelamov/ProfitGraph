namespace ProfitGraph.Venues.Steam.Persistence.Repositories.Contracts;

internal sealed class SteamAccountDb
{
    public Guid CredentialId { get; init; }
    public string AccountFingerprint { get; init; } = string.Empty;
    public string GuardStatus { get; init; } = string.Empty;
    public DateTimeOffset? NextCheckAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}