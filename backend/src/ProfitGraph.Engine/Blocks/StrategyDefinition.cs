using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Blocks;

public sealed record StrategyDefinition
{
    public IReadOnlyList<StrategyParameter> Parameters { get; }
    public IReadOnlyList<StrategyValue> Values { get; }
    public IReadOnlyList<StrategyBlock> Blocks { get; }
    public IReadOnlyList<StrategyWire> Wires { get; }
    public StrategyTemplateKind? TemplateKind { get; }

    public StrategyDefinition(
        IEnumerable<StrategyParameter> parameters,
        IEnumerable<StrategyValue> values,
        IEnumerable<StrategyBlock> blocks,
        IEnumerable<StrategyWire> wires,
        StrategyTemplateKind? templateKind = null)
    {
        Parameters = Array.AsReadOnly(parameters.ToArray());
        Values = Array.AsReadOnly(values.ToArray());
        Blocks = Array.AsReadOnly(blocks.ToArray());
        Wires = Array.AsReadOnly(wires.ToArray());
        TemplateKind = templateKind;
    }
}