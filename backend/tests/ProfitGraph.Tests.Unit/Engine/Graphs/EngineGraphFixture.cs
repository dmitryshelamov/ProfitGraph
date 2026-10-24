using NSubstitute;

using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Composites;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

internal static class EngineGraphFixture
{
    public static IReadOnlyList<INodeDefinition> Definitions { get; } = GraphEngineNodeCatalog.WithResume(
        new INodeDefinition[]
        {
            new ConstantNodeDefinition(), new ParameterNodeDefinition(), new CandleFieldNodeDefinition(), new DataSourceNodeDefinition(), new SmaNodeDefinition(),
            new AtrNodeDefinition(), new PreviousCandleValueNodeDefinition(), new ArithmeticNodeDefinition(), new CompareNodeDefinition(),
            new CrossesAboveNodeDefinition(), new CrossesBelowNodeDefinition(), new AndNodeDefinition(), new OrNodeDefinition(), new NotNodeDefinition(),
            new IsAvailableNodeDefinition(), new OnStartNodeDefinition(), new OnCandleClosedNodeDefinition(),
            new MergeEventsNodeDefinition(),
            new FilterEventNodeDefinition(), new DecisionNodeDefinition()
        }.Concat(BuiltInComposites.Definitions));

    public static GraphNode Node(string id, string type = "CandleField", IReadOnlyDictionary<string, ParamValue>? parameters = null, ValueType? declared = null)
    {
        return new GraphNode(
            id,
            type,
            Definitions.Single(definition => definition.TypeKey == type).SchemaVersion,
            parameters ?? new Dictionary<string, ParamValue>(),
            declared);
    }

    public static FixedParamValue Number(decimal value)
    {
        return new FixedParamValue(new DecimalParamLiteral(value));
    }

    public static FixedParamValue Text(string value)
    {
        return new FixedParamValue(new TextParamLiteral(value));
    }

    public static GraphConnection Wire(string source, string target, string input = "in")
    {
        return new GraphConnection(new PortReference(source, "out"), new PortReference(target, input));
    }

    public static CompiledTradingGraph Compile(StrategyGraph graph)
    {
        TradingGraphCompilationResult result = new TradingGraphCompiler().Compile(graph, Definitions);
        result.Validation.Diagnostics.ShouldBeEmpty();

        return result.Graph.ShouldNotBeNull();
    }

    public static Candle Candle(int index, decimal close)
    {
        return new Candle(DateTimeOffset.UnixEpoch.AddHours(index), close, close, close, close, Volume: 1m);
    }

    public static EventOccurrence Event(Candle candle, bool close = true, int replayIndex = 0)
    {
        var cause = new EventOccurrence(
            id: 1,
            sequence: 1,
            causationId: null,
            EventKind.Start,
            replayIndex,
            candle.OpenTime,
            candle.OpenTime.AddHours(1),
            PricePhase.Open,
            decimal.Max(x: 1m, candle.Close),
            new StartPayload());

        return new EventOccurrence(
            id: 2,
            sequence: 2,
            causationId: 1,
            close ? EventKind.CandleClosed : EventKind.CancelCompleted,
            replayIndex,
            candle.OpenTime,
            candle.OpenTime.AddHours(1),
            close ? PricePhase.Close : PricePhase.High,
            decimal.Max(x: 1m, candle.Close),
            close ? new CandlePayload(candle) : new CancelResultPayload(Cancelled: false, CancelledOrderId: null, "cancel", cause));
    }

    public static INodeDefinition Source(string key, ValueType type, NodeClock clock)
    {
        var source = Substitute.For<INodeDefinition>();
        source.TypeKey.Returns(key);
        source.SchemaVersion.Returns(1);
        source.EngineClock.Returns(clock);
        source.ResolveClock(Arg.Any<NodeClock>()).Returns(clock);
        source.Inputs.Returns(Array.Empty<PortSpec>());
        source.Outputs.Returns(new[] { new PortSpec("out", PortKind.Series, "Value", type) });
        source.Params.Returns(Array.Empty<ParamSpec>());
        source.EngineInputs.Returns(Array.Empty<EngineInputSpec>());

        return source;
    }
}