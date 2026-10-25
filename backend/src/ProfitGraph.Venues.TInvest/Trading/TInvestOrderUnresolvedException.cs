namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     T-Invest gave no definitive answer about an order, so its state is unknown until a lookup by request ID resolves it.
///     The intent retries; nothing is sent again.
/// </summary>
internal sealed class TInvestOrderUnresolvedException() : Exception("The T-Invest order state is not resolved yet.");