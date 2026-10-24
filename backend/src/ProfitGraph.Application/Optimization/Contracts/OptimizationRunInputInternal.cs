using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Optimization.Contracts;

/// <param name="Template">The template whose parameter rules prefilter candidates; null for a custom strategy.</param>
/// <param name="History">The whole prepared history; trials replay only its first <paramref name="InSampleBars" /> candles.</param>
public sealed record OptimizationRunInputInternal(
    StrategyTemplateKind? Template,
    CompiledTradingGraph CompiledGraph,
    PreparedReplay History,
    int InSampleBars,
    ExecutionSettings Settings,
    InstrumentRules Rules,
    OptimizationSearchSpace SearchSpace,
    OptimizationObjective Objective = OptimizationObjective.InSampleReturnPercent,
    OptimizationConstraints? Constraints = null,
    int Budget = OptimizationSearchSpace.DefaultBudget,
    ulong Seed = 0,
    OptimizationSearchMethod Method = OptimizationSearchMethod.Grid);