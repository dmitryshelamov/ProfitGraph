namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Неизменяемый снимок вычислительного графа без координат редактора и HTTP-типов.
/// </summary>
public sealed record StrategyGraph
{
    public StrategyTemplateKind? TemplateKind { get; }
    public IReadOnlyList<GraphNode> Nodes { get; }
    public IReadOnlyList<GraphConnection> Connections { get; }

    public StrategyGraph(IEnumerable<GraphNode> nodes, IEnumerable<GraphConnection> connections, StrategyTemplateKind? templateKind = null)
    {
        TemplateKind = templateKind;
        Nodes = Array.AsReadOnly(nodes.ToArray());
        Connections = Array.AsReadOnly(connections.ToArray());
    }
}