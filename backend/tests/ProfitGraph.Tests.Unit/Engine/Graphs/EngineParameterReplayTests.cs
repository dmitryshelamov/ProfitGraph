using System.Reflection;

using NSubstitute;

using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class EngineParameterReplayTests
{
    [Test]
    public void ChangedWiredParametersInvalidateComputationAndPreserveEarlierDispatchSnapshots()
    {
        var source = new PortReference("price", "out");
        var result = new PortReference("wired", "out");
        var graph = new StrategyGraph(
            [
                new GraphNode("price", "LiveQuote", schemaVersion: 1, new Dictionary<string, ParamValue>()),
                Node("wired", "Constant", new Dictionary<string, ParamValue> { ["value"] = new WiredParamValue(source) }, ValueType.Price)
            ],
            Array.Empty<GraphConnection>());
        CompiledTradingGraph compiled = new TradingGraphCompiler().Compile(graph, Definitions.Append(Source("LiveQuote", ValueType.Price, NodeClock.Dispatch)).ToArray())
            .Graph.ShouldNotBeNull();
        var evaluator = new CandleValueEvaluator(compiled);
        var outputs = new Dictionary<PortReference, EngineValue> { [source] = EngineValue.Numeric(ValueType.Price, value: 10m) };
        EventOccurrence occurrence = Event(Candle(index: 0, close: 10m));
        IReadOnlyDictionary<PortReference, EngineValue> first = evaluator.ReadDispatch(occurrence, outputs);
        evaluator.ReadDispatch(occurrence, outputs)[result].Number.ShouldBe(10m);
        outputs[source] = EngineValue.Numeric(ValueType.Price, value: 20m);
        evaluator.ReadDispatch(occurrence, outputs)[result].Number.ShouldBe(20m);
        outputs.Clear();
        evaluator.ReadDispatch(occurrence, outputs)[result].IsAvailable.ShouldBeFalse();
        outputs[source] = EngineValue.Numeric(ValueType.Price, value: 30m);
        evaluator.ReadDispatch(occurrence, outputs)[result].Number.ShouldBe(30m);
        first[result].Number.ShouldBe(10m);
    }

    [Test]
    public void UnchangedWiredParametersReuseTheSuccessfulPureComputation()
    {
        var source = new PortReference("price", "out");
        INodeDefinition tracked = Source("Tracked", ValueType.Scalar, NodeClock.Dispatch);
        tracked.Params.Returns(new[] { new ParamSpec("value", "Value", ParamValueKind.Number, DefaultValue: 0m) });
        tracked.ResolveParameterType(Arg.Any<GraphNode>(), Arg.Any<ParamSpec>()).Returns(ValueType.Scalar);
        tracked.IsPureComputation.Returns(true);
        tracked.EvaluateEngine(Arg.Any<NodeEngineEvaluationContext>()).Returns(call => call.Arg<NodeEngineEvaluationContext>().Parameters["value"]);
        var graph = new StrategyGraph(
            [
                new GraphNode("price", "LiveQuote", schemaVersion: 1, new Dictionary<string, ParamValue>()),
                new GraphNode("tracked", "Tracked", schemaVersion: 1, new Dictionary<string, ParamValue> { ["value"] = new WiredParamValue(source) })
            ],
            Array.Empty<GraphConnection>());
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(
            graph,
            Definitions.Append(Source("LiveQuote", ValueType.Scalar, NodeClock.Dispatch)).Append(tracked).ToArray());
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        CompiledTradingGraph compiled = compilation.Graph.ShouldNotBeNull();
        var evaluator = new CandleValueEvaluator(compiled);
        var outputs = new Dictionary<PortReference, EngineValue> { [source] = EngineValue.Numeric(ValueType.Scalar, value: 10m) };
        EventOccurrence occurrence = Event(Candle(index: 0, close: 10m));
        evaluator.ReadDispatch(occurrence, outputs);
        evaluator.ReadDispatch(occurrence, outputs);
        tracked.Received(1).EvaluateEngine(Arg.Any<NodeEngineEvaluationContext>());
        outputs[source] = EngineValue.Numeric(ValueType.Scalar, value: 20m);
        evaluator.ReadDispatch(occurrence, outputs);
        tracked.Received(2).EvaluateEngine(Arg.Any<NodeEngineEvaluationContext>());
    }

    [Test]
    public void ReplayUpdatesCannotMutatePublicParameterSnapshots()
    {
        var source = new PortReference("price", "out");
        CompiledTradingNode node = WiredConstant(source);
        var resolver = new EngineParameterResolver();
        Func<CompiledTradingNode, EngineValue[], EngineExecutionInputs, ResolvedEngineParameters> replay = ReplayResolver(resolver);
        var outputs = new Dictionary<PortReference, EngineValue> { [source] = EngineValue.Numeric(ValueType.Price, value: 10m) };
        ResolvedEngineParameters snapshot = resolver.Resolve(node, outputs, EngineExecutionInputs.Empty);
        EngineValue[] slots = [outputs[source]];
        ResolvedEngineParameters view = replay(node, slots, EngineExecutionInputs.Empty);
        outputs[source] = EngineValue.Numeric(ValueType.Price, value: 20m);
        slots[0] = outputs[source];
        replay(node, slots, EngineExecutionInputs.Empty).ShouldBeSameAs(view);
        view["value"].Number.ShouldBe(20m);
        snapshot["value"].Number.ShouldBe(10m);
        resolver.Resolve(node, outputs, EngineExecutionInputs.Empty)["value"].Number.ShouldBe(20m);
    }

    [Test]
    public void ChangingWiredReplayParametersDoNotAllocatePerResolution()
    {
        var source = new PortReference("price", "out");
        CompiledTradingNode node = WiredConstant(source);
        var resolver = new EngineParameterResolver();
        Func<CompiledTradingNode, EngineValue[], EngineExecutionInputs, ResolvedEngineParameters> replay = ReplayResolver(resolver);
        EngineValue[] outputs = [EngineValue.Numeric(ValueType.Price, value: 1m)];

        for (var index = 0; index < 1_000; index++)
        {
            outputs[0] = EngineValue.Numeric(ValueType.Price, index);
            replay(node, outputs, EngineExecutionInputs.Empty);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 10_000; index++)
        {
            outputs[0] = EngineValue.Numeric(ValueType.Price, index);
            replay(node, outputs, EngineExecutionInputs.Empty);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.ShouldBeLessThan(1_024);
        TestContext.Out.WriteLine($"Changing wired parameter replay: {allocated} bytes / 10000 resolutions.");
    }

    [Test]
    public void ReplayStorageIsIsolatedBetweenRunsSharingTheCompiledNodeAndExecutionInputs()
    {
        CompiledTradingNode node = WiredConstant(new PortReference("price", "out"));
        var resolver = new EngineParameterResolver();
        Func<CompiledTradingNode, EngineValue[], EngineExecutionInputs, ResolvedEngineParameters> first = ReplayResolver(resolver);
        Func<CompiledTradingNode, EngineValue[], EngineExecutionInputs, ResolvedEngineParameters> second = ReplayResolver(resolver);
        EngineValue[] firstOutputs = [EngineValue.Numeric(ValueType.Price, value: 10m)];
        EngineValue[] secondOutputs = [EngineValue.Numeric(ValueType.Price, value: 20m)];
        ResolvedEngineParameters firstView = first(node, firstOutputs, EngineExecutionInputs.Empty);
        ResolvedEngineParameters secondView = second(node, secondOutputs, EngineExecutionInputs.Empty);
        secondView.ShouldNotBeSameAs(firstView);
        firstView["value"].Number.ShouldBe(10m);
        secondOutputs[0] = EngineValue.Unavailable(ValueType.Price);
        second(node, secondOutputs, EngineExecutionInputs.Empty)["value"].IsAvailable.ShouldBeFalse();
        first(node, firstOutputs, EngineExecutionInputs.Empty).ShouldBeSameAs(firstView);
        firstView["value"].Number.ShouldBe(10m);
    }

    private static CompiledTradingNode WiredConstant(PortReference source)
    {
        var graph = new StrategyGraph(
            [Node("price"), Node("wired", "Constant", new Dictionary<string, ParamValue> { ["value"] = new WiredParamValue(source) }, ValueType.Price)],
            Array.Empty<GraphConnection>());

        return Compile(graph).Nodes.Single(node => node.Node.Id == "wired");
    }

    private static Func<CompiledTradingNode, EngineValue[], EngineExecutionInputs, ResolvedEngineParameters> ReplayResolver(EngineParameterResolver resolver)
    {
        var storage = new ResolvedEngineParameters?[1];
        var resolve = typeof(EngineParameterResolver).GetMethod("ResolveForReplay", BindingFlags.Instance | BindingFlags.NonPublic)
            .ShouldNotBeNull()
            .CreateDelegate<Func<CompiledTradingNode, EngineValue[], EngineExecutionInputs, ResolvedEngineParameters?[], int, ResolvedEngineParameters>>(resolver);

        return (node, outputs, execution) => resolve(node, outputs, execution, storage, arg5: 0);
    }
}