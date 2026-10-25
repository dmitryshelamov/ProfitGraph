namespace ProfitGraph.Venues.Steam.History;

/// <summary>
///     A market history receipt attributed to one bot order.
/// </summary>
internal sealed record SteamAttribution(string ReceiptId, string ClientOrderId, Guid BotId);