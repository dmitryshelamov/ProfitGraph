using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Compilation;

/// <summary>
///     A strict compilation result; invalid graphs never produce a trading plan.
/// </summary>
public sealed record TradingGraphCompilationResult(CompiledTradingGraph? Graph, GraphValidationResult Validation);