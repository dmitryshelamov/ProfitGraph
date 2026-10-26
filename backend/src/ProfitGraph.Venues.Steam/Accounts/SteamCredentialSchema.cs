using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     A Steam credential is never entered in the web form: the owner imports it from the console or enrolls a new
///     authenticator.
/// </summary>
internal sealed class SteamCredentialSchema : IVenueCredentialSchema
{
    public VenueId Venue => SteamVenue.VenueId;

    public IReadOnlyList<string> Fields => SteamCredentialFields.All;

    public string PublicField => SteamCredentialFields.SteamId;
}