namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     The encrypted fields of a Steam credential: the account's Steam ID, the refresh token of its mobile sign-in and the
///     authenticator's shared secret, identity secret and device ID (ADR 0022).
/// </summary>
internal static class SteamCredentialFields
{
    public const string SteamId = "steamId";
    public const string RefreshToken = "refreshToken";
    public const string SharedSecret = "sharedSecret";
    public const string IdentitySecret = "identitySecret";
    public const string DeviceId = "deviceId";

    /// <summary>
    ///     The purpose of the keyed fingerprint that keeps one Steam account from being stored twice.
    /// </summary>
    public const string AccountFingerprint = "steam-account";

    public static readonly IReadOnlyList<string> All = [SteamId, RefreshToken, SharedSecret, IdentitySecret, DeviceId];
}