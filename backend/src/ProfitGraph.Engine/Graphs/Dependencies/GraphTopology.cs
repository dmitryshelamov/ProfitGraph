using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Graphs.Dependencies;

/// <summary>
///     Собирает зависимости соединений и Wired-параметров и определяет порядок вычисления нод.
///     При равных зависимостях выбирает ноды по ID; возвращает null при цикле.
/// </summary>
public sealed class GraphTopology
{
    public IReadOnlyList<string>? Sort(StrategyGraph graph, IReadOnlyList<INodeDefinition>? definitions = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        Dictionary<string, int> incoming = graph.Nodes.Select(node => node.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        Dictionary<string, List<string>> outgoing = incoming.Keys
            .ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        IEnumerable<(string SourceNodeId, string TargetNodeId)> dependencies = graph.Connections
            .Where(connection => definitions is null || !GraphDependencyPolicy.IsDeferred(graph, definitions, connection))
            .Select(connection => (SourceNodeId: connection.Source.NodeId, TargetNodeId: connection.Target.NodeId))
            .Concat(
                graph.Nodes.SelectMany(node => node.Parameters
                    .Where(pair => GraphDependencyPolicy.IsActiveParameter(
                        graph,
                        node,
                        pair.Key,
                        definitions?.SingleOrDefault(definition => definition.TypeKey == node.TypeKey)))
                    .Select(pair => pair.Value)
                    .OfType<WiredParamValue>()
                    .Select(value => (SourceNodeId: value.Source.NodeId, TargetNodeId: node.Id))))
            .Distinct();

        foreach ((string source, string target) in dependencies)
        {
            // Missing node references are diagnosed by GraphValidator, not by topology ordering.
            if (!incoming.ContainsKey(source) || !incoming.ContainsKey(target))
            {
                continue;
            }

            incoming[target]++;
            outgoing[source].Add(target);
        }

        var ready = new SortedSet<string>(incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key), StringComparer.Ordinal);
        var order = new List<string>();

        while (ready.Count > 0)
        {
            string id = ready.Min!;
            ready.Remove(id);
            order.Add(id);

            foreach (string target in outgoing[id])
            {
                if (--incoming[target] == 0)
                {
                    ready.Add(target);
                }
            }
        }

        return order.Count == incoming.Count ? order.AsReadOnly() : null;
    }
}