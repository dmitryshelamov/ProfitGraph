using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Instruments;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.Backtesting.History;

/// <summary>
///     Captures the instrument rules and prepares the history of a frozen configuration, with job progress; work that
///     resumes prepares the history again against the rules it already captured.
/// </summary>
public sealed class BacktestHistoryLoader(InstrumentRulesCaptureService rules, HistoryPreparationService history)
{
    public async Task<LoadedBacktestHistory> Load(BacktestConfiguration configuration, CancellationToken cancellationToken)
    {
        CandleSeries series = configuration.Series;
        InstrumentRulesSnapshotInternal captured = await rules.Capture(series.Venue, series.Symbol, cancellationToken);
        PreparedHistory prepared = await Prepare(configuration, cancellationToken);
        InstrumentRulesDefinitionInternal definition = captured.Definition;
        var info = new BacktestHistoryInfo(
            prepared.Identity,
            prepared.HistoryFrom,
            prepared.From,
            prepared.To,
            prepared.WarmupCount,
            prepared.Candles.Count - prepared.WarmupCount,
            new InstrumentRulesSnapshotId(captured.Id),
            definition.BaseAsset,
            definition.QuoteAsset,
            definition.Rules,
            ClosedIntervals(prepared, prepared.HistoryFrom, prepared.From),
            ClosedIntervals(prepared, prepared.From, prepared.To));

        return new LoadedBacktestHistory(prepared, definition, info);
    }

    /// <summary>
    ///     The grid intervals of [from, to) that the history's market closures cover (ADR 0023, TC1).
    /// </summary>
    private static int ClosedIntervals(PreparedHistory prepared, DateTimeOffset from, DateTimeOffset to)
    {
        long step = prepared.Series.Timeframe.Duration.Ticks;

        return (int)prepared.Closures.Sum(closure =>
            Math.Max(val1: 0L, (Min(closure.To, to).UtcTicks - Max(closure.From, from).UtcTicks) / step));
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left < right ? left : right;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
    {
        return left > right ? left : right;
    }

    public Task<PreparedHistory> Prepare(BacktestConfiguration configuration, CancellationToken cancellationToken)
    {
        return history.Prepare(
            configuration.Series,
            configuration.From,
            configuration.To,
            configuration.WarmupCandles,
            configuration.ObservedAt,
            reportProgress: true,
            cancellationToken);
    }
}