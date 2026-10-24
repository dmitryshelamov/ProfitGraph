using System.Security.Cryptography;
using System.Text;

using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Graphs.Composites;

public sealed class CompositeExpander
{
    public CompositeExpansion Expand(StrategyGraph graph, IReadOnlyList<INodeDefinition> definitions)
    {
        Dictionary<string, IExpandableNodeDefinition> composites =
            definitions.OfType<IExpandableNodeDefinition>().ToDictionary(definition => definition.TypeKey, StringComparer.Ordinal);

        if (!graph.Nodes.Any(node => composites.TryGetValue(node.TypeKey, out IExpandableNodeDefinition? definition) && definition.SchemaVersion == node.SchemaVersion))
        {
            return new CompositeExpansion(graph, new Dictionary<string, string>(), new Dictionary<string, string>(), Array.Empty<GraphDiagnostic>());
        }

        var nodes = new List<GraphNode>();
        var edges = new List<GraphConnection>();
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var innerIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var inputs = new Dictionary<PortReference, IReadOnlyList<PortReference>>();
        var outputs = new Dictionary<PortReference, PortReference>();
        var diagnostics = new List<GraphDiagnostic>();
        HashSet<string> occupied = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);

        foreach (IGrouping<string, GraphNode> group in graph.Nodes.GroupBy(node => node.Id, StringComparer.Ordinal)
                     .Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
        {
            diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.DuplicateNodeId, "Node IDs must be non-empty and unique.", group.Key));
        }

        foreach (GraphNode outer in graph.Nodes)
        {
            if (!composites.TryGetValue(outer.TypeKey, out IExpandableNodeDefinition? definition) || outer.SchemaVersion != definition.SchemaVersion)
            {
                nodes.Add(outer);

                continue;
            }

            var ids = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (GraphNode inner in definition.Graph.Nodes)
            {
                string stem = "composite:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(outer.Id))) + ":" + inner.Id;
                string candidate = stem;
                var suffix = 0;

                while (!occupied.Add(candidate))
                {
                    candidate = stem + ":" + ++suffix;
                }

                ids.Add(inner.Id, candidate);
                owners.Add(candidate, outer.Id);
                innerIds.Add(candidate, inner.Id);
            }

            PortReference Remap(PortReference reference) => new PortReference(ids[reference.NodeId], reference.PortKey);

            foreach (GraphNode inner in definition.Graph.Nodes)
            {
                Dictionary<string, ParamValue> parameters = inner.Parameters.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value is WiredParamValue wired ? new WiredParamValue(Remap(wired.Source)) : pair.Value,
                    StringComparer.Ordinal);
                var identities = new Dictionary<string, PortReference>(StringComparer.Ordinal);

                foreach ((string key, IReadOnlyList<PortReference> targets) in definition.ParameterMappings)
                {
                    ParamValue? value = outer.Parameters.GetValueOrDefault(key);

                    foreach (PortReference target in targets.Where(target => target.NodeId == inner.Id))
                    {
                        if (value is not null)
                        {
                            parameters[target.PortKey] = value;
                        }

                        identities[target.PortKey] = new PortReference(outer.Id, key);
                    }
                }

                nodes.Add(new GraphNode(ids[inner.Id], inner.TypeKey, inner.SchemaVersion, parameters, inner.DeclaredValueType, inner.EvaluationMode, identities));
            }

            edges.AddRange(definition.Graph.Connections.Select(edge => new GraphConnection(Remap(edge.Source), Remap(edge.Target))));

            foreach ((string key, IReadOnlyList<PortReference> targets) in definition.InputMappings)
            {
                inputs[new PortReference(outer.Id, key)] = targets.Select(Remap).ToArray();
            }

            foreach ((string key, PortReference source) in definition.OutputMappings)
            {
                outputs[new PortReference(outer.Id, key)] = Remap(source);
            }

            foreach (string key in outer.Parameters.Keys.Where(key => !definition.ParameterMappings.ContainsKey(key)))
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.UnknownParameter, $"Unknown composite parameter '{key}'.", outer.Id, ParamKey: key));
            }
        }

        PortReference Source(PortReference reference) => outputs.GetValueOrDefault(reference, reference);

        foreach (GraphConnection edge in graph.Connections)
        {
            if (inputs.TryGetValue(edge.Target, out IReadOnlyList<PortReference>? targets))
            {
                edges.AddRange(targets.Select(target => new GraphConnection(Source(edge.Source), target)));
            }
            else
            {
                edges.Add(new GraphConnection(Source(edge.Source), edge.Target));
            }
        }

        GraphNode[] mapped = nodes.Select(node => new GraphNode(
                node.Id,
                node.TypeKey,
                node.SchemaVersion,
                node.Parameters.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value is WiredParamValue wired ? new WiredParamValue(Source(wired.Source)) : pair.Value,
                    StringComparer.Ordinal),
                node.DeclaredValueType,
                node.EvaluationMode,
                node.ParameterIdentities))
            .ToArray();

        return new CompositeExpansion(new StrategyGraph(mapped, edges, graph.TemplateKind), owners, innerIds, diagnostics.AsReadOnly());
    }
}