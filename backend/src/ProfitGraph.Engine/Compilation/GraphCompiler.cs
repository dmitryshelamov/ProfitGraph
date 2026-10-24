using System.Globalization;

using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Composites;
using ProfitGraph.Engine.Graphs.Dependencies;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Compilation;

/// <summary>
///     Связывает валидный граф с определениями нод и строит план; данные свечей здесь не нужны.
/// </summary>
public sealed class GraphCompiler
{
    public GraphCompilationResult Compile(StrategyGraph graph, IReadOnlyList<INodeDefinition> definitions)
    {
        GraphValidationResult validation = new GraphValidator().Validate(graph, definitions);

        if (!validation.IsValid)
        {
            return new GraphCompilationResult(Graph: null, validation);
        }

        graph = new CompositeExpander().Expand(graph, definitions).Graph;
        Dictionary<string, GraphNode> nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        Dictionary<string, INodeDefinition> types = definitions.ToDictionary(definition => definition.TypeKey, StringComparer.Ordinal);
        GraphNode? unsupported = graph.Nodes.FirstOrDefault(node => !types[node.TypeKey].SupportsPhase1 || types[node.TypeKey].IsAction ||
            HasEngineOnlyBinding(graph, node, types[node.TypeKey], types) ||
            node.DeclaredValueType is ValueType.Boolean or ValueType.Tag or ValueType.OrderId or ValueType.RejectionReason ||
            node.Parameters.Values.Any(value => value is DecimalOptimizeParamValue or
                FixedParamValue { Value: DecimalParamLiteral or BooleanParamLiteral or OrderIdParamLiteral or RejectionReasonParamLiteral }));
        IReadOnlyList<string>? order = new GraphTopology().Sort(graph);

        if (unsupported is not null || order is null)
        {
            var diagnostic = new GraphDiagnostic(
                GraphDiagnosticCode.UnsupportedPhase1Graph,
                "This graph requires a trading plan and cannot execute in the Phase 1 interpreter.",
                unsupported?.Id);

            return new GraphCompilationResult(Graph: null, new GraphValidationResult(Array.AsReadOnly(new[] { diagnostic })));
        }

        CompiledNode[] compiled = order.Select(id => new CompiledNode(
                nodes[id],
                types[nodes[id].TypeKey],
                Array.AsReadOnly(
                    graph.Connections.Where(connection => connection.Target.NodeId == id)
                        .Select(connection => new CompiledInputBinding(connection.Target.PortKey, connection.Source))
                        .ToArray())))
            .ToArray();

        return new GraphCompilationResult(new CompiledGraph(Array.AsReadOnly(compiled)), validation);
    }

    private static bool HasEngineOnlyBinding(StrategyGraph graph, GraphNode node, INodeDefinition definition, IReadOnlyDictionary<string, INodeDefinition> types)
    {
        foreach (EngineInputSpec input in definition.EngineInputs)
        {
            GraphConnection? connection = graph.Connections.SingleOrDefault(edge => edge.Target == new PortReference(node.Id, input.Key));
            ParamSpec? parameter = definition.Params.SingleOrDefault(spec => spec.Key == input.Key);

            if (connection is null)
            {
                if (!CanResolveLegacyParameter(node, definition, input))
                {
                    return true;
                }

                continue;
            }

            if (parameter is null)
            {
                continue;
            }

            if (parameter.ValueKind != ParamValueKind.Number || node.Parameters.GetValueOrDefault(input.Key) is not WiredParamValue wired ||
                wired.Source != connection.Source)
            {
                return true;
            }

            GraphNode source = graph.Nodes.Single(source => source.Id == connection.Source.NodeId);
            PortSpec output = types[source.TypeKey].Outputs.Single(port => port.Key == connection.Source.PortKey);

            if (output.Kind != PortKind.Series)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanResolveLegacyParameter(GraphNode node, INodeDefinition definition, EngineInputSpec input)
    {
        ParamSpec? parameter = definition.Params.SingleOrDefault(spec => spec.Key == input.Key);

        if (parameter?.ValueKind != ParamValueKind.Number || input.ValueType is not (ValueType.Scalar or ValueType.Integer))
        {
            return false;
        }

        ParamValue? value = node.Parameters.GetValueOrDefault(input.Key);

        if (value is not null)
        {
            return value is FixedParamValue { Value: NumberParamLiteral } or OptimizeParamValue or WiredParamValue;
        }

        if (parameter.DefaultValue is not (sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal))
        {
            return false;
        }

        if (input.DefaultValue is not { } engineDefault)
        {
            return false;
        }

        var legacyDefault = Convert.ToDouble(parameter.DefaultValue, CultureInfo.InvariantCulture);

        return EngineLiteralResolver.Resolve(new NumberParamLiteral(legacyDefault), input.ValueType)?.Number == engineDefault.Number;
    }
}