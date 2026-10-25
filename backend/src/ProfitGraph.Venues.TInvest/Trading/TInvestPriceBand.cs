namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The exchange's current price limits of a share per share, <c>limit_down</c> to <c>limit_up</c>.
/// </summary>
internal sealed record TInvestPriceBand(decimal Low, decimal High);