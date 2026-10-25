namespace ProfitGraph.Venues.TInvest.Transport;

/// <summary>
///     The Invest API service groups that are limited separately per token.
/// </summary>
internal enum TInvestMethodGroup
{
    Users,
    Instruments,
    MarketData,
    Orders,
    Operations
}