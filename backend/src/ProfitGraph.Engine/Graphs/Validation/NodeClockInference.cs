using ProfitGraph.Engine.Graphs.Dependencies;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Graphs.Validation;

/// <summary>
///     Infers sampling clocks from immutable definitions and active graph dependencies.
/// </summary>
public sealed class NodeClockInference
{
    public IReadOnlyDictionary<string, NodeClock> Resolve(StrategyGraph graph, IReadOnlyList<INodeDefinition> definitions, List<GraphDiagnostic> diagnostics)
    {
        Dictionary<string, INodeDefinition> types = definitions.ToDictionary(definition => definition.TypeKey, StringComparer.Ordinal);
        Dictionary<string, GraphNode> nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var clocks = new Dictionary<string, NodeClock>(StringComparer.Ordinal);

        foreach (string id in new GraphTopology().Sort(graph, definitions)!)
        {
            GraphNode node = nodes[id];
            INodeDefinition definition = types[node.TypeKey];
            GraphConnection[] connections = graph.Connections.Where(connection => connection.Target.NodeId == id &&
                    !GraphDependencyPolicy.IsDeferred(graph, definitions, connection))
                .ToArray();
            IEnumerable<string> dependencies = connections.Select(connection => connection.Source.NodeId)
                .Concat(
                    node.Parameters
                        .Where(pair => pair.Value is WiredParamValue && GraphDependencyPolicy.IsActiveParameter(graph, node, pair.Key, definition))
                        .Select(pair => ((WiredParamValue)pair.Value).Source.NodeId));
            NodeClock[] sourceClocks = dependencies.Select(source => clocks.GetValueOrDefault(source, NodeClock.Dispatch)).ToArray();
            NodeClock inherited = sourceClocks.Any(clock => clock is NodeClock.Dispatch or NodeClock.Event or NodeClock.Action)
                ? NodeClock.Dispatch
                : sourceClocks.Contains(NodeClock.CandleClocked)
                    ? NodeClock.CandleClocked
                    : NodeClock.Static;
            NodeClock clock = definition.ResolveClock(inherited);
            clocks[id] = clock;

            if (definition.SampleInputKeys.Count > 0)
            {
                foreach (GraphConnection sample in connections.Where(connection => definition.SampleInputKeys.Contains(connection.Target.PortKey)))
                {
                    if (clocks.GetValueOrDefault(sample.Source.NodeId, NodeClock.Dispatch) != NodeClock.CandleClocked)
                    {
                        diagnostics.Add(
                            new GraphDiagnostic(
                                GraphDiagnosticCode.SmaInputNotCandleClocked,
                                "An indicator sample must be derived from the closed-candle clock.",
                                id,
                                sample.Target.PortKey));
                    }
                }
            }
        }

        return clocks;
    }
}