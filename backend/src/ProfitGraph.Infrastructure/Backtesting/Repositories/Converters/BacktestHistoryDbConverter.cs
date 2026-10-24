using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Converters;

internal static class BacktestHistoryDbConverter
{
    public static BacktestHistoryInfo? ToHistory(this IBacktestHistoryDb row)
    {
        return row.HistoryIdentity is null
            ? null
            : new BacktestHistoryInfo(
                row.HistoryIdentity,
                row.HistoryFrom!.Value.ToUniversalTime(),
                row.ReplayFrom!.Value.ToUniversalTime(),
                row.ReplayTo!.Value.ToUniversalTime(),
                row.WarmupCount!.Value,
                row.ReplayCount!.Value,
                new InstrumentRulesSnapshotId(row.RulesSnapshotId!.Value),
                row.BaseAsset!,
                row.QuoteAsset!,
                new InstrumentRules(
                    row.QuantityStep!.Value,
                    row.PriceTick!.Value,
                    row.MinQuantity!.Value,
                    row.MinNotional!.Value,
                    row.BasePrecision!.Value,
                    row.QuotePrecision!.Value,
                    row.BuyFeeAsset == "base" ? FeeAsset.Base : FeeAsset.Quote,
                    row.ExactQuoteFees!.Value));
    }
}