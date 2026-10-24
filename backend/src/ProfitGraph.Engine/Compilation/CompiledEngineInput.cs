using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Compilation;

/// <summary>
///     Frozen active source for a typed engine input. A connection overrides its stored inline setting.
/// </summary>
public sealed record CompiledEngineInput(EngineInputSpec Spec, PortReference? Source, ParamValue? Inline)
{
    internal int SourceSlot { get; init; } = -1;
}