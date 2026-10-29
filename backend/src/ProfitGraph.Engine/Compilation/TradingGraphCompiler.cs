using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Dependencies;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Compilation;

/// <summary>
///     Compiles strict typed bindings and lifecycle boundaries without invoking replay.
/// </summary>
public sealed class TradingGraphCompiler
{
    public TradingGraphCompilationResult Compile(StrategyGraph graph, IReadOnlyList<INodeDefinition> definitions)
    {
        StrictGraphAnalysis analysis = new GraphValidator().AnalyzeStrict(graph, definitions);
        GraphValidationResult validation = analysis.Validation;
        graph = analysis.Graph;

        if (!validation.IsValid)
        {
            validation = new GraphValidationResult(
                Array.AsReadOnly(
                    validation.Diagnostics.Select(diagnostic => diagnostic.Code == GraphDiagnosticCode.Cycle
                            ? diagnostic with { Code = GraphDiagnosticCode.ValueOrImmediateActionCycle }
                            : diagnostic)
                        .ToArray()));

            return new TradingGraphCompilationResult(Graph: null, validation);
        }

        // A long's buy spends quote and a short's cover gives base; one plan cannot do both (ADR 0061).
        if (graph.Nodes.Any(node => node.TypeKey is nameof(TradingNodeBehavior.MarketCover) or nameof(TradingNodeBehavior.LimitCover)) &&
            graph.Nodes.FirstOrDefault(node => node.TypeKey is nameof(TradingNodeBehavior.MarketBuy) or nameof(TradingNodeBehavior.LimitBuy)
                or nameof(TradingNodeBehavior.Grid)) is { } buy)
        {
            return new TradingGraphCompilationResult(
                Graph: null,
                new GraphValidationResult(
                    Array.AsReadOnly(new[] { new GraphDiagnostic(GraphDiagnosticCode.InvalidNodeMetadata, "A plan that covers a short cannot buy long.", buy.Id) })));
        }

        IReadOnlyDictionary<PortReference, ValueType> outputTypes = analysis.OutputTypes!;
        IReadOnlyDictionary<string, NodeClock> clocks = analysis.Clocks!;
        int warmup = analysis.WarmupCandles;
        Dictionary<string, GraphNode> nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        Dictionary<string, INodeDefinition> types = definitions.ToDictionary(definition => definition.TypeKey, StringComparer.Ordinal);
        IReadOnlyList<string> order = new GraphTopology().Sort(graph, definitions)!;
        var slots = new Dictionary<PortReference, int>();

        foreach (string id in order)
        {
            foreach (PortReference reference in outputTypes.Keys.Where(reference => reference.NodeId == id)
                         .OrderBy(reference => reference.PortKey, StringComparer.Ordinal))
            {
                slots.Add(reference, slots.Count);
            }
        }

        var compiled = new List<CompiledTradingNode>();

        for (var rank = 0; rank < order.Count; rank++)
        {
            GraphNode node = nodes[order[rank]];
            INodeDefinition definition = types[node.TypeKey];
            var bindings = new List<CompiledEngineInput>();

            foreach (EngineInputSpec input in definition.EngineInputs)
            {
                GraphConnection? connection = graph.Connections.SingleOrDefault(connection => connection.Target == new PortReference(node.Id, input.Key));
                ParamValue? inline = connection is null ? node.Parameters.GetValueOrDefault(input.Key) : null;
                PortReference? source = connection?.Source ?? (inline as WiredParamValue)?.Source;
                bindings.Add(
                    new CompiledEngineInput(input, source, source is null ? inline : null)
                    {
                        SourceSlot = source is null ? -1 : slots[source]
                    });
            }

            compiled.Add(
                new CompiledTradingNode(
                    node,
                    definition,
                    rank,
                    clocks[node.Id],
                    definition.Params,
                    bindings,
                    graph.Connections.Where(connection => connection.Target.NodeId == node.Id)
                        .Select(connection => new CompiledInputBinding(connection.Target.PortKey, connection.Source)
                        {
                            SourceSlot = slots[connection.Source]
                        }),
                    outputTypes.Where(pair => pair.Key.NodeId == node.Id).ToDictionary(pair => pair.Key.PortKey, pair => pair.Value, StringComparer.Ordinal),
                    definition.TradingBehavior,
                    definition.TradingBehavior == TradingNodeBehavior.EventFields
                        ? EventFieldCatalog.Find(NodeTextSettingResolver.Resolve(node, definition.Params, "field"))
                        : null,
                    definition.AllowedEvaluationModes,
                    slots));
        }

        return new TradingGraphCompilationResult(
            new CompiledTradingGraph(compiled, graph.Connections.Where(connection => GraphDependencyPolicy.IsDeferred(graph, definitions, connection)), warmup),
            validation);
    }
}