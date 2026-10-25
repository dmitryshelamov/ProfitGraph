using ProfitGraph.Application.Backtesting.StartBacktest;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting;

/// <summary>
///     Freezes the configuration of a started run (stage 3 plan, S3-5); an optimization freezes its backtest the same way.
/// </summary>
internal static class BacktestFreezing
{
    public static BacktestConfiguration Freeze(
        StartBacktestCommand request,
        CandleSeries series,
        IReadOnlyDictionary<string, decimal> parameters,
        CompiledTradingGraph plan,
        DateTimeOffset now)
    {
        DateTimeOffset latest = series.Timeframe.FloorToGrid(now);

        if (request.From >= latest)
        {
            throw new ApplicationValidationException([new InputError("invalid_request", "From", "The start must come before the latest closed candle.")]);
        }

        OutOfSampleSplit split = request.OutOfSampleFraction is { } fraction ? OutOfSampleSplit.Create(fraction) : OutOfSampleSplit.Default;

        if (request.From is { } from)
        {
            CheckSplit(split, ((request.To is { } to && to < latest ? to : latest).UtcTicks - from.UtcTicks) / series.Timeframe.Duration.Ticks);
        }

        return new BacktestConfiguration(
            series,
            request.From,
            request.To,
            now,
            parameters,
            Settings(request),
            split,
            plan.WarmupCandles,
            BacktestResult.ExecutionPolicyVersion,
            GraphCompilationFingerprint.Calculate(plan));
    }

    private static ExecutionSettings Settings(StartBacktestCommand request)
    {
        return request.SellerFees is { } fees
            ? new ExecutionSettings(request.StartingCapital, request.Slippage, new SellerFeeSchedule(fees.Rates, fees.Minimum))
            : new ExecutionSettings(request.StartingCapital, request.Slippage, request.LimitFeeRate, request.MarketFeeRate);
    }

    /// <summary>
    ///     With a known start the holdout is checked at once; without one the range is known only when the Worker prepares it.
    /// </summary>
    private static void CheckSplit(OutOfSampleSplit split, long replayCount)
    {
        try
        {
            split.Split(checked((int)replayCount));
        }
        catch (Exception exception) when (exception is ArgumentException or SampleSplitException or OverflowException)
        {
            throw new ApplicationValidationException([new InputError(BacktestErrorCodes.InvalidSplit, "OutOfSampleFraction", exception.Message)]);
        }
    }
}