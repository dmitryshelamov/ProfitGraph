using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Graphs.Composites;

public interface IExpandableNodeDefinition : INodeDefinition
{
    StrategyGraph Graph { get; }
    IReadOnlyDictionary<string, IReadOnlyList<PortReference>> InputMappings { get; }
    IReadOnlyDictionary<string, PortReference> OutputMappings { get; }
    IReadOnlyDictionary<string, IReadOnlyList<PortReference>> ParameterMappings { get; }
}