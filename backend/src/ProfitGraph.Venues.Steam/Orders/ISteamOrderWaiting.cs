namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Tells the core why a bot's Steam order waits locally, or that it no longer does (ADR 0022, waiting codes).
/// </summary>
internal interface ISteamOrderWaiting
{
    Task Report(SteamOrderJournalEntry entry, SteamWaitingCode? code, DateTimeOffset? until, CancellationToken cancellationToken);
}