namespace ProfitGraph.Venues.Steam.History;

/// <summary>
///     A receipt at its absolute position in the account's history, counted from the oldest event (Profit Fox Designer ADR
///     0087): the event at newest-first index <c>i</c> of a history of <c>T</c> events is at <c>T - 1 - i</c>.
/// </summary>
internal sealed record SteamStoredReceipt(long Position, SteamMarketReceipt Receipt);