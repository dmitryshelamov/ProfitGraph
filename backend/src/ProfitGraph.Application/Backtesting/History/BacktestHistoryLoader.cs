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
            definition.Rules);

        return new LoadedBacktestHistory(prepared, definition, info);
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