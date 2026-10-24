using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Converters;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Converters;

internal static class BacktestReadDbConverter
{
    public static BacktestLedgerEntryInternal ToLedgerEntry(this BacktestStoredFactDb row)
    {
        return BacktestJsonSerializer.Deserialize<BacktestLedgerEntry>(row.Snapshot).ToInternal();
    }

    public static BacktestDiagnosticInternal ToDiagnostic(this BacktestStoredFactDb row)
    {
        return BacktestJsonSerializer.Deserialize<BacktestDiagnostic>(row.Snapshot).ToInternal(row.Sequence);
    }

    public static BacktestObservationInternal ToInternal(
        this BacktestCandleDb row,
        IReadOnlyList<BacktestLedgerEntryInternal> ledger,
        IReadOnlyList<BacktestDiagnosticInternal> diagnostics)
    {
        return new BacktestObservationInternal(
            row.Sequence,
            row.OpenTime.ToUniversalTime(),
            row.CandleIndex,
            row.Phase,
            row.MarketPrice,
            row.Open,
            row.High,
            row.Low,
            row.Close,
            row.Volume,
            row.Equity,
            row.UnrealizedPnl,
            row.TotalPnl,
            row.RealizedPnl,
            row.QuoteTotal,
            row.QuoteReserved,
            row.BaseTotal,
            row.BaseReserved,
            row.CostBasis,
            row.FeesBase,
            row.FeesQuote,
            row.LastBuyPrice,
            row.ActiveOrders,
            ledger,
            diagnostics);
    }

    public static BacktestChartPointInternal ToInternal(this BacktestChartPointDb row)
    {
        return new BacktestChartPointInternal(
            row.FirstSequence,
            row.LastSequence,
            row.FirstOpenTime.ToUniversalTime(),
            row.LastOpenTime.ToUniversalTime(),
            row.Open,
            row.High,
            row.Low,
            row.Close,
            row.Volume,
            row.Equity,
            row.MinEquity,
            row.MaxEquity,
            row.TotalPnl,
            row.BuyFills,
            row.SellFills);
    }

    public static BacktestSummaryInternal ToInternal(this BacktestSummaryDb row)
    {
        return new BacktestSummaryInternal(
            row.Id,
            row.StrategyId,
            row.StrategyName,
            row.StrategyVersion,
            row.Venue,
            row.Symbol,
            row.Timeframe,
            row.CreatedAt.ToUniversalTime(),
            row.Status,
            row.Outcome,
            row.QuoteAsset,
            row.TotalPnl,
            row.ReturnPercent,
            row.MaxDrawdownPercent);
    }

    public static BacktestRunStateInternal ToInternal(this BacktestRunStateDb row)
    {
        return new BacktestRunStateInternal(
            row.Status,
            row.Outcome,
            row.LastSequence,
            row.LedgerCount,
            row.CandleCount,
            row.DiagnosticCount,
            row.ChartBucketSize);
    }
}