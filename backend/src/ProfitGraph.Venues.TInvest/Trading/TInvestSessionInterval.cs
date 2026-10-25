namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     One exchange interval of a trading day, and whether it is a holiday (weekend) session.
/// </summary>
internal sealed record TInvestSessionInterval(DateTimeOffset From, DateTimeOffset To, bool Holiday);