namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Tells the core why a bot's Steam order waits locally, or that it no longer does (ADR 0022, waiting codes), and asks
///     it whether an order held outside a runner turn may be sent now.
/// </summary>
internal interface ISteamOrderWaiting
{
    Task Report(SteamOrderJournalEntry entry, SteamWaitingCode? code, DateTimeOffset? until, CancellationToken cancellationToken);

    /// <summary>
    ///     Whether the bot may send now: it should run, is healthy, trading is not halted and its lease is live with its submit
    ///     gate open. False when the core cannot tell.
    /// </summary>
    Task<bool> MayRelease(Guid botId, CancellationToken cancellationToken);
}