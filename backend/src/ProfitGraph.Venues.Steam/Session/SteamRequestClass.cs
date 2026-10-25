namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     What a request is for, in the order the account queue serves them (Profit Fox Designer ADR 0083): order writes and
///     confirmations first, history last and only when nothing else waits.
/// </summary>
internal enum SteamRequestClass
{
    Write,
    Status,
    Account,
    Quote,
    History
}