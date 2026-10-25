namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     How a <see cref="SteamOrderJournalState.Done" /> order ended.
/// </summary>
internal enum SteamOrderOutcome
{
    Rejected,
    Cancelled,
    Filled
}