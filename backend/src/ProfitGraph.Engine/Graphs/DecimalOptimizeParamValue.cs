namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Exact decimal optimization bounds; selection is supplied separately for a replay.
/// </summary>
public sealed record DecimalOptimizeParamValue(decimal Min, decimal Max, decimal Step) : ParamValue;