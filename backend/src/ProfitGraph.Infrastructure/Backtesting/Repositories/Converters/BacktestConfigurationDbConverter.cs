using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Converters;

internal static class BacktestConfigurationDbConverter
{
    public static BacktestConfigurationDb ToDb(this BacktestConfiguration configuration)
    {
        return new BacktestConfigurationDb
        {
            Venue = configuration.Series.Venue.Value,
            Symbol = configuration.Series.Symbol.Value,
            Timeframe = configuration.Series.Timeframe.Key,
            From = configuration.From,
            To = configuration.To,
            ObservedAt = configuration.ObservedAt,
            Parameters = configuration.Parameters.ToDictionary(StringComparer.Ordinal),
            StartingCapital = configuration.Settings.StartingCapital,
            Slippage = configuration.Settings.Slippage,
            LimitFeeRate = configuration.Settings.LimitFeeRate,
            MarketFeeRate = configuration.Settings.MarketFeeRate,
            OutOfSampleFraction = configuration.Split.Fraction,
            WarmupCandles = configuration.WarmupCandles,
            ExecutionPolicy = configuration.ExecutionPolicy,
            Fingerprint = configuration.Fingerprint
        };
    }

    public static BacktestConfiguration ToDomain(this BacktestConfigurationDb row)
    {
        return new BacktestConfiguration(
            new CandleSeries(new VenueId(row.Venue), new InstrumentSymbol(row.Symbol), Timeframe.Parse(row.Timeframe)),
            row.From?.ToUniversalTime(),
            row.To?.ToUniversalTime(),
            row.ObservedAt.ToUniversalTime(),
            row.Parameters,
            new ExecutionSettings(row.StartingCapital, row.Slippage, row.LimitFeeRate, row.MarketFeeRate),
            OutOfSampleSplit.Create(row.OutOfSampleFraction),
            row.WarmupCandles,
            row.ExecutionPolicy,
            row.Fingerprint);
    }
}