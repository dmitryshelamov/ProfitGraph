using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Graphs.Dependencies;

/// <summary>
///     Separates deferred order lifecycle routes from immediate dependencies and ignores overridden inline sources.
/// </summary>
public static class GraphDependencyPolicy
{
    public static bool IsDeferred(StrategyGraph graph, IReadOnlyList<INodeDefinition> definitions, GraphConnection connection)
    {
        GraphNode? node = graph.Nodes.FirstOrDefault(node => node.Id == connection.Source.NodeId);
        INodeDefinition? definition = definitions.SingleOrDefault(definition => definition.TypeKey == node?.TypeKey);

        return definition?.IsAction == true && connection.Source.PortKey is "filled" or "cancelled";
    }

    public static bool IsActiveParameter(StrategyGraph graph, GraphNode node, string key, INodeDefinition? definition)
    {
        return definition?.EngineInputs.Any(input => input.Key == key) != true
            || !graph.Connections.Any(connection => connection.Target == new PortReference(node.Id, key));
    }
}