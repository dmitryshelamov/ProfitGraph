using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Eager checked financial summary, separate from the exact committed account balances.
/// </summary>
internal readonly record struct AccountValuation(decimal Equity, decimal UnrealizedPnl, decimal TotalPnl)
{
    public static AccountValuation Capture(AccountSnapshot account, decimal reachedPrice)
    {
        return Capture(account.StartingCapital, account.QuoteTotal, account.BaseTotal, account.CostBasis, reachedPrice);
    }

    public static AccountValuation Capture(Portfolio portfolio, decimal reachedPrice)
    {
        return Capture(portfolio.StartingCapital, portfolio.QuoteTotal, portfolio.BaseTotal, portfolio.CostBasis, reachedPrice);
    }

    private static AccountValuation Capture(decimal startingCapital, decimal quoteTotal, decimal baseTotal, decimal costBasis, decimal reachedPrice)
    {
        try
        {
            decimal markedBase = checked(baseTotal * reachedPrice);
            decimal equity = checked(quoteTotal + markedBase);

            return new AccountValuation(equity, checked(markedBase - costBasis), checked(equity - startingCapital));
        }
        catch (OverflowException exception)
        {
            throw new BacktestValuationException(exception);
        }
    }
}