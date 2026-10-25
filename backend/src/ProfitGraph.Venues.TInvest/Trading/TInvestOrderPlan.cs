namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     What an engine order asks T-Invest for: whole lots and, for a limit order, its price on the share's tick.
/// </summary>
internal sealed record TInvestOrderPlan(long Lots, decimal? Price);