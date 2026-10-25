namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     The Steam-specific state of one credential: the keyed fingerprint of its Steam ID, which keeps the same account from
///     being stored twice without storing the ID, and its authenticator status with the time of the next check.
/// </summary>
internal sealed record SteamAccount(
    Guid CredentialId,
    string AccountFingerprint,
    SteamGuardStatus GuardStatus,
    DateTimeOffset? NextCheckAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static SteamAccount Imported(Guid credentialId, string accountFingerprint, DateTimeOffset now)
    {
        return new SteamAccount(credentialId, accountFingerprint, SteamGuardStatus.Checking, now, now, now);
    }
}