using System.Globalization;

using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Dependencies;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Computes preceding-candle demand from compositional windows and statically bounded periods.
/// </summary>
public sealed class WarmUpCalculator
{
    public int Calculate(CompiledTradingGraph graph)
    {
        return graph.WarmupCandles;
    }

    public int Calculate(
        StrategyGraph graph,
        IReadOnlyList<INodeDefinition> definitions,
        IReadOnlyDictionary<string, NodeClock> clocks,
        List<GraphDiagnostic> diagnostics)
    {
        Dictionary<string, GraphNode> nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        Dictionary<string, INodeDefinition> types = definitions.ToDictionary(definition => definition.TypeKey, StringComparer.Ordinal);
        var demands = new Dictionary<string, int>(StringComparer.Ordinal);
        var bounds = new Dictionary<string, (decimal Min, decimal Max)?>(StringComparer.Ordinal);

        (decimal Min, decimal Max)? Bound(ParamValue value)
        {
            return value switch
            {
                FixedParamValue fixedValue when EngineLiteralResolver.Resolve(fixedValue.Value, ValueType.Integer) is { } number => (number.Number, number.Number),
                DecimalOptimizeParamValue range => (range.Min, range.Max),
                WiredParamValue wired => bounds.GetValueOrDefault(wired.Source.NodeId),
                _ => null
            };
        }

        foreach (string id in new GraphTopology().Sort(graph, definitions)!)
        {
            GraphNode node = nodes[id];
            INodeDefinition definition = types[node.TypeKey];
            GraphConnection[] connections = graph.Connections.Where(connection => connection.Target.NodeId == id &&
                    !GraphDependencyPolicy.IsDeferred(graph, definitions, connection))
                .ToArray();
            int demand = connections.Select(connection => connection.Source.NodeId)
                .Concat(
                    node.Parameters
                        .Where(pair => pair.Value is WiredParamValue && GraphDependencyPolicy.IsActiveParameter(graph, node, pair.Key, definition))
                        .Select(pair => ((WiredParamValue)pair.Value).Source.NodeId))
                .Select(source => demands.GetValueOrDefault(source))
                .DefaultIfEmpty()
                .Max();

            try
            {
                if (clocks[id] == NodeClock.Static)
                {
                    (decimal Min, decimal Max)? InputBound(string key)
                    {
                        GraphConnection? connection = connections.SingleOrDefault(edge => edge.Target.PortKey == key);

                        return connection is not null ? bounds.GetValueOrDefault(connection.Source.NodeId)
                            : node.Parameters.TryGetValue(key, out ParamValue? literal) ? Bound(literal) : null;
                    }

                    bounds[id] = definition.ResolveStaticBounds(node, Bound, InputBound);
                }

                if (definition.WarmupPeriodKey is { Length: > 0 } periodKey)
                {
                    ParamValue period = node.Parameters.GetValueOrDefault(periodKey) ?? new FixedParamValue(
                        new DecimalParamLiteral(Convert.ToDecimal(definition.Params.Single(spec => spec.Key == periodKey).DefaultValue, CultureInfo.InvariantCulture)));
                    GraphConnection? periodConnection = connections.SingleOrDefault(edge => edge.Target.PortKey == periodKey);
                    (decimal Min, decimal Max)? range = periodConnection is null ? Bound(period) : bounds.GetValueOrDefault(periodConnection.Source.NodeId);
                    ParamSpec spec = definition.Params.Single(parameter => parameter.Key == periodKey);

                    if (range is null || range.Value.Min < 1m || decimal.Truncate(range.Value.Min) != range.Value.Min ||
                        decimal.Truncate(range.Value.Max) != range.Value.Max || range.Value.Max > int.MaxValue ||
                        (spec.MaxValue.HasValue && range.Value.Max > (decimal)spec.MaxValue.Value))
                    {
                        diagnostics.Add(
                            new GraphDiagnostic(
                                GraphDiagnosticCode.UnboundedIndicatorPeriod,
                                "Indicator period requires a positive, finite, integral static bound.",
                                id,
                                ParamKey: periodKey));
                    }
                    else
                    {
                        demand = checked(demand + ((int)range.Value.Max + definition.WarmupOffset));
                    }
                }
                else if (definition.WarmupOffset != 0)
                {
                    demand = checked(demand + definition.WarmupOffset);
                }
            }
            catch (OverflowException)
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.WarmupOverflow, "Warm-up demand exceeds the supported candle count.", id));
            }

            demands[id] = demand;
        }

        int maximum = demands.Values.DefaultIfEmpty().Max();

        if (!graph.Nodes.Any(node => types[node.TypeKey].IsIndicator) || diagnostics.Count > 0)
        {
            return maximum;
        }

        try
        {
            // Node demands are zero-based closed-candle readiness indices. Startup reads precede candle zero's close.
            return checked(maximum + 1);
        }
        catch (OverflowException)
        {
            string id = demands.First(pair => pair.Value == maximum).Key;
            diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.WarmupOverflow, "Warm-up demand exceeds the supported candle count.", id));

            return maximum;
        }
    }
}