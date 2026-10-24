using ProfitGraph.Engine.Graphs.Dependencies;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Validation;

/// <summary>
///     Выводит конкретные типы полиморфных портов по настройкам и зависимостям валидного ациклического графа.
/// </summary>
public sealed class GraphValueTypeResolver
{
    public IReadOnlyDictionary<PortReference, ValueType> Resolve(
        StrategyGraph graph,
        IReadOnlyList<INodeDefinition> definitions,
        List<GraphDiagnostic> diagnostics)
    {
        var outputs = new Dictionary<PortReference, ValueType>();
        Dictionary<string, INodeDefinition> types = definitions.ToDictionary(definition => definition.TypeKey, StringComparer.Ordinal);
        Dictionary<string, GraphNode> nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        IReadOnlyList<string> order = new GraphTopology().Sort(graph, definitions)!;

        foreach (string id in order)
        {
            GraphNode node = nodes[id];
            INodeDefinition definition = types[node.TypeKey];

            ValueType? Input(string key)
            {
                GraphConnection? connection = graph.Connections.SingleOrDefault(connection => connection.Target == new PortReference(id, key));

                if (connection is not null)
                {
                    return outputs.TryGetValue(connection.Source, out ValueType type)
                        ? type
                        : types[nodes[connection.Source.NodeId].TypeKey].Outputs.Single(port => port.Key == connection.Source.PortKey).ValueType;
                }

                EngineInputSpec? spec = definition.EngineInputs.SingleOrDefault(input => input.Key == key);

                if (node.Parameters.TryGetValue(key, out ParamValue? parameter) && parameter is WiredParamValue wired)
                {
                    return outputs.TryGetValue(wired.Source, out ValueType wiredType) ? wiredType : null;
                }

                return spec?.ValueType;
            }

            ValueType? numericOutput = definition.ResolveOutputType(node, Input, diagnostics);

            foreach (PortSpec input in definition.Inputs)
            {
                ValueType? actual = Input(input.Key);

                if (actual.HasValue && input.ValueType is { } expected && actual.Value != expected)
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.IncompatibleUnits, $"Expected {expected}, got {actual}.", id, input.Key));
                }
            }

            foreach ((string key, ParamValue value) in node.Parameters)
            {
                if (value is not WiredParamValue wired || !GraphDependencyPolicy.IsActiveParameter(graph, node, key, definition) ||
                    !outputs.TryGetValue(wired.Source, out ValueType actual))
                {
                    continue;
                }

                ValueType expected = definition.EngineInputs.SingleOrDefault(input => input.Key == key)?.ValueType
                    ?? (key == "period" ? ValueType.Integer : node.DeclaredValueType ?? ValueType.Scalar);

                if (actual != expected)
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.IncompatibleUnits, $"Wired parameter requires {expected}, got {actual}.", id, ParamKey: key));
                }
            }

            foreach (PortSpec output in definition.Outputs)
            {
                ValueType? type = output.Kind == PortKind.Series && numericOutput.HasValue ? numericOutput : output.ValueType;

                if (type.HasValue)
                {
                    outputs[new PortReference(id, output.Key)] = type.Value;
                }
            }
        }

        return outputs;
    }
}