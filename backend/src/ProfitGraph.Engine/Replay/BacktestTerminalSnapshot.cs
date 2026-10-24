using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Cleaned terminal balances; financial summary is unavailable only when exact reached-price valuation is unrepresentable.
/// </summary>
public sealed record BacktestTerminalSnapshot
{
    public BacktestOutcome Outcome { get; init; }
    public int ProcessedCandles { get; }
    public ReachedMarketContext Context { get; }
    public AccountSnapshot Account { get; }
    public BacktestDiagnostic? Diagnostic { get; init; }
    public decimal? Equity { get; }
    public decimal? UnrealizedPnl { get; }
    public decimal? TotalPnl { get; }

    public BacktestTerminalSnapshot(
        BacktestOutcome outcome,
        int processedCandles,
        ReachedMarketContext context,
        AccountSnapshot account,
        BacktestDiagnostic? diagnostic)
    {
        Outcome = outcome;
        ProcessedCandles = processedCandles;
        Context = context;
        Account = account;
        Diagnostic = diagnostic;

        try
        {
            AccountValuation valuation = AccountValuation.Capture(account, context.MarketPrice);
            Equity = valuation.Equity;
            UnrealizedPnl = valuation.UnrealizedPnl;
            TotalPnl = valuation.TotalPnl;
        }
        catch (BacktestValuationException exception)
        {
            Outcome = BacktestOutcome.Failed;
            Diagnostic = new BacktestDiagnostic("ValuationOverflow", exception.Message, CandleIndex: context.CandleIndex);
        }
    }

    private BacktestTerminalSnapshot(
        BacktestOutcome outcome,
        int processedCandles,
        ReachedMarketContext context,
        AccountSnapshot account,
        BacktestDiagnostic? diagnostic,
        decimal? equity,
        decimal? unrealizedPnl,
        decimal? totalPnl)
    {
        if (!Enum.IsDefined(outcome) || processedCandles < 0 ||
            equity.HasValue != unrealizedPnl.HasValue || equity.HasValue != totalPnl.HasValue ||
            (!equity.HasValue && (outcome != BacktestOutcome.Failed || diagnostic is null)))
        {
            throw new ArgumentException("Invalid persisted terminal summary.");
        }

        Outcome = outcome;
        ProcessedCandles = processedCandles;
        Context = context;
        Account = account;
        Diagnostic = diagnostic;
        Equity = equity;
        UnrealizedPnl = unrealizedPnl;
        TotalPnl = totalPnl;
    }

    /// <summary>
    ///     Restores exact persisted financial values, including explicit unavailable valuation, without recalculation.
    /// </summary>
    public static BacktestTerminalSnapshot Restore(
        BacktestOutcome outcome,
        int processedCandles,
        ReachedMarketContext context,
        AccountSnapshot account,
        BacktestDiagnostic? diagnostic,
        decimal? equity,
        decimal? unrealizedPnl,
        decimal? totalPnl)
    {
        return new BacktestTerminalSnapshot(outcome, processedCandles, context, account, diagnostic, equity, unrealizedPnl, totalPnl);
    }
}