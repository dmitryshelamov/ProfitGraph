namespace ProfitGraph.Venues.Steam.Enrollment;

/// <summary>
///     A started password sign-in: Steam's client and request IDs, the account's Steam ID and the confirmations Steam allows
///     (2 is a code from email, 3 a code from an existing authenticator).
/// </summary>
internal sealed record SteamSignIn(string ClientId, string RequestId, string SteamId, IReadOnlyList<int> Confirmations);