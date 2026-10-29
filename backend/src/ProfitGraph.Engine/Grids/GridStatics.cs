using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;

namespace ProfitGraph.Engine.Grids;

/// <summary>
///     The static settings of every grid of a compiled strategy, read before a run or a bot starts (ADR 0058, sections 8 and
///     9). A grid whose settings are unavailable or out of range reads as null.
/// </summary>
public static class GridStatics
{
    public static IReadOnlyDictionary<string, GridSettings?> Read(CompiledTradingGraph graph, EngineExecutionInputs selections)
    {
        ArgumentNullException.ThrowIfNull(graph);
        CompiledTradingNode[] grids = graph.ActionNodes.Where(node => node.TradingBehavior == TradingNodeBehavior.Grid).ToArray();

        if (grids.Length == 0)
        {
            return new Dictionary<string, GridSettings?>();
        }

        IReadOnlyDictionary<PortReference, EngineValue> values;

        try
        {
            values = new CandleValueEvaluator(graph, selections).ReadStaticValues();
        }
        catch (GraphExecutionException)
        {
            return grids.ToDictionary(grid => grid.Node.Id, _ => (GridSettings?)null, StringComparer.Ordinal);
        }

        return grids.ToDictionary(grid => grid.Node.Id, grid => Settings(grid, values), StringComparer.Ordinal);
    }

    private static GridSettings? Settings(CompiledTradingNode grid, IReadOnlyDictionary<PortReference, EngineValue> values)
    {
        var numbers = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach ((string key, _) in GridNodeDefinition.Settings)
        {
            CompiledEngineInput? input = grid.EngineInputs.FirstOrDefault(candidate => candidate.Spec.Key == key);

            if (input?.Source is not { } source || !values.TryGetValue(source, out EngineValue value) || !value.IsAvailable ||
                !UnitAlgebra.IsNumeric(value.ValueType))
            {
                return null;
            }

            numbers[key] = value.Number;
        }

        return GridSettings.Create(
            numbers["levels"],
            numbers["step"],
            numbers["profitStep"],
            numbers["lot"],
            numbers["maxOpenBuys"],
            numbers["shiftThreshold"],
            numbers["anchorCeiling"],
            numbers["stopBelow"]);
    }
}