namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Steam could not prove an order's state now: the intent is retried, and a write that may have been sent is never
///     sent again on this evidence. The message names the cause, never provider text.
/// </summary>
internal sealed class SteamOrderUnresolvedException(string message) : Exception(message);