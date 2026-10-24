using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Application.Strategies.Contracts;

/// <summary>
///     A strategy in the list, with its newest run if it has one.
/// </summary>
public sealed record StrategySummaryInternal(
    Guid Id,
    string Name,
    int CurrentVersion,
    DateTimeOffset UpdatedAt,
    StrategyTemplateKind? TemplateKind,
    BacktestSummaryInternal? LatestRun);