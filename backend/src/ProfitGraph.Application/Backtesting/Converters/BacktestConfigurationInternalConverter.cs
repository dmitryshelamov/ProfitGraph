using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Application.Backtesting.Converters;

public static class BacktestConfigurationInternalConverter
{
    public static BacktestConfigurationInternal ToInternal(this BacktestConfiguration configuration)
    {
        return new BacktestConfigurationInternal(
            configuration.Series.Venue.Value,
            configuration.Series.Symbol.Value,
            configuration.Series.Timeframe.Key,
            configuration.From,
            configuration.To,
            configuration.ObservedAt,
            configuration.Parameters,
            configuration.Settings.StartingCapital,
            configuration.Settings.Slippage,
            configuration.Settings.LimitFeeRate,
            configuration.Settings.MarketFeeRate,
            configuration.Split.Fraction,
            configuration.WarmupCandles,
            configuration.Settings.SellerFees is { } fees ? new SellerFeeScheduleInternal([.. fees.Rates], fees.Minimum, fees.Increment) : null,
            configuration.Settings.SettlementHold is { } hold ? (long)hold.TotalSeconds : null,
            configuration.Settings.LimitFill.ToString());
    }
}