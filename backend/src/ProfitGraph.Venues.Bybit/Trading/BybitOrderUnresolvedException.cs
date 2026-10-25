namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Bybit could not prove an order's state: the intent is retried, and a submit that may have been sent is never placed
///     again on this evidence.
/// </summary>
internal sealed class BybitOrderUnresolvedException() : Exception("The Bybit order state is unknown.");
