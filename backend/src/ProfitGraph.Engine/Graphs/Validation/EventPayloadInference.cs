using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Graphs.Validation;

/// <summary>
///     Propagates possible event payloads through forwarding nodes, including deferred lifecycle feedback.
/// </summary>
public sealed class EventPayloadInference
{
    public void Validate(StrategyGraph graph, IReadOnlyList<INodeDefinition> definitions, List<GraphDiagnostic> diagnostics)
    {
        Dictionary<string, INodeDefinition> types = definitions.ToDictionary(definition => definition.TypeKey, StringComparer.Ordinal);
        var payloads = new Dictionary<PortReference, HashSet<EventPayloadType>>();

        foreach (GraphNode node in graph.Nodes)
        {
            foreach (PortSpec port in types[node.TypeKey].Outputs.Where(port => port.Kind == PortKind.Event))
            {
                var set = new HashSet<EventPayloadType>();

                if (port.EventPayloadType.HasValue)
                {
                    set.Add(port.EventPayloadType.Value);
                }

                payloads.Add(new PortReference(node.Id, port.Key), set);
            }
        }

        bool changed;

        do
        {
            changed = false;

            foreach (GraphNode node in graph.Nodes.Where(node => node.TypeKey is NodeTypeKeys.MergeEvents or NodeTypeKeys.FilterEvent))
            {
                foreach (HashSet<EventPayloadType> target in payloads.Where(pair => pair.Key.NodeId == node.Id).Select(pair => pair.Value))
                {
                    foreach (GraphConnection connection in graph.Connections.Where(connection => connection.Target.NodeId == node.Id))
                    {
                        if (!payloads.TryGetValue(connection.Source, out HashSet<EventPayloadType>? source))
                        {
                            continue;
                        }

                        foreach (EventPayloadType type in source)
                        {
                            changed |= target.Add(type);
                        }
                    }
                }
            }
        }
        while (changed);

        foreach (GraphNode node in graph.Nodes.Where(node => types[node.TypeKey].TradingBehavior == TradingNodeBehavior.EventFields))
        {
            string key = NodeTextSettingResolver.Resolve(node, types[node.TypeKey].Params, "field");
            EventFieldSpec? field = EventFieldCatalog.Find(key);
            GraphConnection? connection = graph.Connections.SingleOrDefault(connection => connection.Target == new PortReference(node.Id, "event"));

            if (field is null || connection is null || !payloads.TryGetValue(connection.Source, out HashSet<EventPayloadType>? source) ||
                source.Count == 0 || source.Any(type => type != field.PayloadType))
            {
                diagnostics.Add(
                    new GraphDiagnostic(
                        GraphDiagnosticCode.IncompatibleEventPayload,
                        "Selected event field requires a compatible known payload on every route.",
                        node.Id,
                        "event"));
            }
        }
    }
}