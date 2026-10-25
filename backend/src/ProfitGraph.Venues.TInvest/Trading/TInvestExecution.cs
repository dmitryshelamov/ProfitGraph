namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     What has executed: lots, quote in roubles from the trade stages, and the commission T-Invest reported.
/// </summary>
internal sealed record TInvestExecution(long Lots, decimal Quote, decimal Fee)
{
    public static readonly TInvestExecution None = new TInvestExecution(Lots: 0, Quote: 0m, Fee: 0m);

    public TInvestExecution Add(TInvestExecution other)
    {
        return new TInvestExecution(Lots + other.Lots, Quote + other.Quote, Fee + other.Fee);
    }
}