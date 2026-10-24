using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Application.Backtesting.Converters;

public static class BacktestHistoryInternalConverter
{
    public static BacktestHistoryInternal ToInternal(this BacktestHistoryInfo history)
    {
        return new BacktestHistoryInternal(
            history.Identity,
            history.HistoryFrom,
            history.From,
            history.To,
            history.WarmupCount,
            history.ReplayCount,
            history.RulesSnapshotId.Value,
            history.BaseAsset,
            history.QuoteAsset,
            history.Rules);
    }
}