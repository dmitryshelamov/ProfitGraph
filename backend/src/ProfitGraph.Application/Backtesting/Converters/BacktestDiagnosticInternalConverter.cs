using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting.Converters;

public static class BacktestDiagnosticInternalConverter
{
    public static BacktestDiagnosticInternal ToInternal(this BacktestDiagnostic diagnostic, long sequence)
    {
        return new BacktestDiagnosticInternal(
            sequence,
            diagnostic.Code,
            diagnostic.Message,
            diagnostic.NodeId,
            diagnostic.CandleIndex,
            diagnostic.Tag,
            diagnostic.RejectionReason?.ToString());
    }
}