using ProfitGraph.Application.Backtesting.Execution;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Converters;

internal static class BacktestFactDbConverter
{
    public static BacktestLedgerDb ToDb(this BacktestLedgerEntry entry)
    {
        return new BacktestLedgerDb
        {
            Sequence = entry.Sequence,
            CandleIndex = entry.Context.CandleIndex,
            Kind = entry.Kind.ToString(),
            OrderId = entry.Order?.Id,
            Snapshot = BacktestJsonSerializer.ToElement(entry)
        };
    }

    public static BacktestCandleDb ToDb(this BacktestCandleObservation observation)
    {
        CandleSnapshot snapshot = observation.Snapshot;
        AccountSnapshot account = snapshot.Account;

        return new BacktestCandleDb
        {
            Sequence = snapshot.Sequence,
            OpenTime = snapshot.Context.Candle.OpenTime,
            CandleIndex = snapshot.Context.CandleIndex,
            Phase = snapshot.Context.Phase.ToString(),
            MarketPrice = snapshot.Context.MarketPrice,
            Open = snapshot.Context.Candle.Open,
            High = snapshot.Context.Candle.High,
            Low = snapshot.Context.Candle.Low,
            Close = snapshot.Context.Candle.Close,
            Volume = snapshot.Context.Candle.Volume,
            Equity = snapshot.Equity,
            UnrealizedPnl = snapshot.UnrealizedPnl,
            TotalPnl = snapshot.TotalPnl,
            RealizedPnl = account.RealizedPnl,
            QuoteTotal = account.QuoteTotal,
            QuoteReserved = account.QuoteReserved,
            BaseTotal = account.BaseTotal,
            BaseReserved = account.BaseReserved,
            CostBasis = account.CostBasis,
            FeesBase = account.FeesBase,
            FeesQuote = account.FeesQuote,
            LastBuyPrice = account.LastBuyPrice,
            ActiveOrders = account.Orders.Count,
            LedgerThrough = observation.LedgerThrough,
            DiagnosticThrough = observation.DiagnosticThrough
        };
    }

    public static BacktestDiagnosticDb ToDb(this BacktestDiagnosticRecord record)
    {
        return new BacktestDiagnosticDb
        {
            Sequence = record.Sequence,
            Code = record.Diagnostic.Code,
            CandleIndex = record.Diagnostic.CandleIndex,
            Snapshot = BacktestJsonSerializer.ToElement(record.Diagnostic)
        };
    }
}