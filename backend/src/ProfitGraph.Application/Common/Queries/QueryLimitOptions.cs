namespace ProfitGraph.Application.Common.Queries;

/// <summary>
///     Section <c>QueryLimits</c>: the largest page each read returns or accepts as its <c>limit</c>.
/// </summary>
public sealed class QueryLimitOptions
{
    public const string SectionName = "QueryLimits";

    public int StrategyBacktests { get; init; } = 100;
    public int BacktestFills { get; init; } = 500;
    public int BacktestOrders { get; init; } = 500;
    public int BacktestRanking { get; init; } = 100;
    public int BacktestDiagnostics { get; init; } = 500;
    public int BacktestObservations { get; init; } = 100;
    public int OptimizationTrials { get; init; } = 500;
    public int StrategyOptimizations { get; init; } = 100;
    public int Optimizations { get; init; } = 100;
    public int Candles { get; init; } = 10_000;
    public int Strategies { get; init; } = 500;

    /// <summary>
    ///     The most candles one page of a bot's chart covers: one venue read of history per page at most (ADR 0026).
    /// </summary>
    public int BotChartCandles { get; init; } = 1000;

    public int BotChartFills { get; init; } = 1000;

    public bool IsValid()
    {
        return new[]
        {
            StrategyBacktests, BacktestFills, BacktestOrders, BacktestRanking, BacktestDiagnostics, BacktestObservations, OptimizationTrials,
            StrategyOptimizations, Optimizations, Candles, Strategies, BotChartCandles, BotChartFills
        }.All(limit => limit > 0);
    }
}