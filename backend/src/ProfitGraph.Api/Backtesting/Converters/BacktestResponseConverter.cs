using ProfitGraph.Api.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Numbers;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Metrics;

namespace ProfitGraph.Api.Backtesting.Converters;

public static class BacktestResponseConverter
{
    public static BacktestRunResponse ToResponse(this BacktestRunInternal run)
    {
        BacktestConfigurationInternal configuration = run.Configuration;

        return new BacktestRunResponse(
            run.Id,
            run.StrategyId,
            run.StrategyVersion,
            run.State.Status,
            run.CreatedAt,
            configuration.ToResponse(),
            run.Stage,
            run.History?.ToResponse(),
            run.Result is { } result
                ? new BacktestResultResponse(
                    result.Outcome,
                    result.ProcessedCandles,
                    Text(result.Equity),
                    Text(result.TotalPnl),
                    result.FailureCode,
                    result.FailureMessage,
                    result.Metrics is { } metrics
                        ? new BacktestMetricsResponse(
                            metrics.Full.ToResponse(),
                            metrics.InSample.ToResponse(),
                            metrics.OutOfSample is { } holdout ? holdout.ToResponse() : null)
                        : null)
                : null,
            run.State.LedgerCount,
            run.State.CandleCount,
            run.State.DiagnosticCount,
            run.State.ChartBucketSize);
    }

    public static BacktestConfigurationResponse ToResponse(this BacktestConfigurationInternal configuration)
    {
        return new BacktestConfigurationResponse(
            configuration.Venue,
            configuration.Symbol,
            configuration.Timeframe,
            configuration.From,
            configuration.To,
            configuration.ObservedAt,
            configuration.Parameters.ToDictionary(pair => pair.Key, pair => Text(pair.Value), StringComparer.Ordinal),
            Text(configuration.StartingCapital),
            Text(configuration.Slippage),
            Text(configuration.LimitFeeRate),
            Text(configuration.MarketFeeRate),
            Text(configuration.OutOfSampleFraction),
            configuration.WarmupCandles,
            configuration.SellerFees is { } fees ? new SellerFeeScheduleResponse([.. fees.Rates.Select(Text)], Text(fees.Minimum), fees.Increment) : null,
            configuration.SettlementHoldSeconds);
    }

    public static BacktestHistoryResponse ToResponse(this BacktestHistoryInternal history)
    {
        return new BacktestHistoryResponse(
            history.Identity,
            history.HistoryFrom,
            history.From,
            history.To,
            history.WarmupCount,
            history.ReplayCount,
            history.RulesSnapshotId,
            history.BaseAsset,
            history.QuoteAsset,
            history.Rules.ToResponse());
    }

    public static BacktestObservationResponse ToResponse(this BacktestObservationInternal observation)
    {
        return new BacktestObservationResponse(
            observation.Sequence,
            observation.OpenTime,
            observation.CandleIndex,
            observation.Phase,
            Text(observation.MarketPrice),
            Text(observation.Open),
            Text(observation.High),
            Text(observation.Low),
            Text(observation.Close),
            Text(observation.Volume),
            Text(observation.Equity),
            Text(observation.UnrealizedPnl),
            Text(observation.TotalPnl),
            Text(observation.RealizedPnl),
            Text(observation.QuoteTotal),
            Text(observation.QuoteReserved),
            Text(observation.BaseTotal),
            Text(observation.BaseReserved),
            Text(observation.CostBasis),
            Text(observation.FeesBase),
            Text(observation.FeesQuote),
            Text(observation.LastBuyPrice),
            observation.ActiveOrders,
            [.. observation.Ledger.Select(entry => entry.ToResponse())],
            [.. observation.Diagnostics.Select(diagnostic => diagnostic.ToResponse())]);
    }

    public static BacktestLedgerEntryResponse ToResponse(this BacktestLedgerEntryInternal entry)
    {
        return new BacktestLedgerEntryResponse(
            entry.Sequence,
            entry.Kind,
            entry.CandleIndex,
            entry.CandleOpenTime,
            entry.Phase,
            Text(entry.MarketPrice),
            entry.Order is { } order
                ? new BacktestOrderResponse(
                    order.Id,
                    order.Tag,
                    order.Side,
                    order.Type,
                    Text(order.Quantity),
                    Text(order.LimitPrice),
                    Text(order.QuoteBudget),
                    Text(order.Reservation),
                    order.Status,
                    order.OriginNodeId)
                : null,
            entry.Fill is { } fill
                ? new BacktestFillResponse(
                    fill.OrderId,
                    fill.Tag,
                    fill.Side,
                    fill.Type,
                    Text(fill.Price),
                    Text(fill.GrossBase),
                    Text(fill.BaseChange),
                    Text(fill.QuoteChange),
                    Text(fill.Fee),
                    fill.FeeAsset,
                    Text(fill.RealizedPnl))
                : null,
            entry.Rejection is { } rejection
                ? new BacktestRejectionResponse(rejection.Reason, rejection.Message, rejection.Side, rejection.Type, rejection.Tag, rejection.OrderId)
                : null);
    }

    public static BacktestDiagnosticResponse ToResponse(this BacktestDiagnosticInternal diagnostic)
    {
        return new BacktestDiagnosticResponse(
            diagnostic.Sequence,
            diagnostic.Code,
            diagnostic.Message,
            diagnostic.NodeId,
            diagnostic.CandleIndex,
            diagnostic.Tag,
            diagnostic.RejectionReason);
    }

    public static BacktestPageResponse<TResponse> ToResponse<TItem, TResponse>(this BacktestPageInternal<TItem> page, Func<TItem, TResponse> convert)
    {
        return new BacktestPageResponse<TResponse>([.. page.Items.Select(convert)], page.Next);
    }

    public static BacktestChartResponse ToResponse(this BacktestChartInternal chart)
    {
        return new BacktestChartResponse(
            chart.BucketSize,
            [
                .. chart.Points.Select(point => new BacktestChartPointResponse(
                    point.FirstSequence,
                    point.LastSequence,
                    point.FirstOpenTime,
                    point.LastOpenTime,
                    Text(point.Open),
                    Text(point.High),
                    Text(point.Low),
                    Text(point.Close),
                    Text(point.Volume),
                    Text(point.Equity),
                    Text(point.MinEquity),
                    Text(point.MaxEquity),
                    Text(point.TotalPnl),
                    point.BuyFills,
                    point.SellFills))
            ]);
    }

    public static BacktestSummaryResponse ToResponse(this BacktestSummaryInternal summary)
    {
        return new BacktestSummaryResponse(
            summary.Id,
            summary.StrategyId,
            summary.StrategyName,
            summary.StrategyVersion,
            summary.Venue,
            summary.Symbol,
            summary.Timeframe,
            summary.CreatedAt,
            summary.Status,
            summary.Outcome,
            summary.QuoteAsset,
            Text(summary.TotalPnl),
            Text(summary.ReturnPercent),
            Text(summary.MaxDrawdownPercent),
            summary.OptimizationId);
    }

    public static BacktestFacetsResponse ToResponse(this BacktestFacetsInternal facets)
    {
        return new BacktestFacetsResponse(
            Facets(facets.Quotes),
            Facets(facets.Statuses),
            Facets(facets.Origins),
            Facets(facets.Venues),
            Facets(facets.Timeframes),
            [.. facets.Strategies.Select(strategy => new BacktestStrategyFacetResponse(strategy.Id, strategy.Name, strategy.Count))]);
    }

    public static BacktestSummaryPageResponse ToResponse(this BacktestSummaryPageInternal page)
    {
        return new BacktestSummaryPageResponse([.. page.Items.Select(item => item.ToResponse())], page.Next);
    }

    public static InstrumentRulesResponse ToResponse(this InstrumentRules rules)
    {
        return new InstrumentRulesResponse(
            Text(rules.QuantityStep),
            Text(rules.PriceTick),
            Text(rules.MinQuantity),
            Text(rules.MinNotional),
            rules.BasePrecision,
            rules.QuotePrecision);
    }

    public static SampleMetricsResponse ToResponse(this SampleMetrics sample)
    {
        return new SampleMetricsResponse(
            sample.StartIndex,
            sample.EndExclusive,
            sample.ObservedCandles,
            Text(sample.BoundaryEquity),
            Text(sample.EndEquity),
            Text(sample.TotalPnl),
            Text(sample.RealizedPnl),
            Text(sample.UnrealizedPnl),
            Text(sample.ReturnPercent),
            Text(sample.FeesBase),
            Text(sample.FeesQuote),
            Text(sample.MaxDrawdownPercent),
            sample.Executions);
    }

    private static string Text(decimal value)
    {
        return ExactDecimalText.Format(value);
    }

    private static string? Text(decimal? value)
    {
        return value is { } number ? ExactDecimalText.Format(number) : null;
    }

    private static BacktestFacetResponse[] Facets(IEnumerable<BacktestFacetInternal> values)
    {
        return [.. values.Select(value => new BacktestFacetResponse(value.Value, value.Count))];
    }
}