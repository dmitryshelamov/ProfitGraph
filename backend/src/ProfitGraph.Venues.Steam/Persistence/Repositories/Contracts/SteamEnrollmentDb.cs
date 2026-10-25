namespace ProfitGraph.Venues.Steam.Persistence.Repositories.Contracts;

internal sealed class SteamEnrollmentDb
{
    public Guid Id { get; init; }
    public Guid OwnerId { get; init; }
    public string Step { get; init; } = string.Empty;
    public string KeyId { get; init; } = string.Empty;
    public byte[] Nonce { get; init; } = [];
    public byte[] Ciphertext { get; init; } = [];
    public byte[] Tag { get; init; } = [];
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}