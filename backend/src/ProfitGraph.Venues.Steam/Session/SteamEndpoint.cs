namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     The Steam hosts ProfitGraph talks to: the Community site (market, inventory, confirmations), the login host that
///     finalizes a sign-in, and the Web API (server time).
/// </summary>
internal enum SteamEndpoint
{
    Community,
    Login,
    Api
}