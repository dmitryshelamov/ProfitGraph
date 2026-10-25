namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     A request waiting in an account queue, ordered by class and then by arrival.
/// </summary>
internal sealed record SteamQueuedRequest(SteamRequestClass Class, long Sequence);