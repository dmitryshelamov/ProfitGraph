namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     The Steam state of stored credentials (ADR 0022).
/// </summary>
internal interface ISteamAccountStore
{
    Task<SteamAccount?> Get(Guid credentialId, CancellationToken cancellationToken);
}