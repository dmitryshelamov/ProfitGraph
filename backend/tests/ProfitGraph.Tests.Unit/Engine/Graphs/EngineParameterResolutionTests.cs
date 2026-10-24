using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class EngineParameterResolutionTests
{
    [Test]
    public void ExtremeDecimalGridSelectionsMatchEnumerationWithoutSubtractionOverflow()
    {
        var graph = new StrategyGraph(
            [
                Node(
                    "choice",
                    "Constant",
                    new Dictionary<string, ParamValue>
                    {
                        ["value"] = new DecimalOptimizeParamValue(decimal.MinValue, decimal.MaxValue, decimal.MaxValue)
                    })
            ],
            Array.Empty<GraphConnection>());
        CompiledTradingGraph compiled = Compile(graph);
        OptimizationTrial[] trials = OptimizationSearchSpace.FromGraph(compiled).Enumerate().ToArray();
        trials.Select(trial => trial.Selections.OptimizedValues.Single().Value).ShouldBe(new[] { decimal.MinValue, 0m, decimal.MaxValue });
        var resolver = new EngineParameterResolver();

        foreach (OptimizationTrial trial in trials)
        {
            resolver.ValidateSelections(compiled, trial.Selections).Diagnostics.ShouldBeEmpty();
            resolver.Resolve(compiled.Nodes.Single(), new Dictionary<PortReference, EngineValue>(), trial.Selections)["value"]
                .Number
                .ShouldBe(trial.Selections.OptimizedValues.Single().Value);
        }
    }

    [Test]
    public void SelectionValidationRejectsOffGridValuesWhoseDecimalSubtractionWouldRound()
    {
        var graph = new StrategyGraph(
            [
                Node(
                    "choice",
                    "Constant",
                    new Dictionary<string, ParamValue>
                    {
                        ["value"] = new DecimalOptimizeParamValue(Min: 0.1m, decimal.MaxValue, Step: 1m)
                    })
            ],
            Array.Empty<GraphConnection>());
        CompiledTradingGraph compiled = Compile(graph);
        var selections = new EngineExecutionInputs(new Dictionary<PortReference, decimal> { [new PortReference("choice", "value")] = decimal.MaxValue });
        new EngineParameterResolver().ValidateSelections(compiled, selections).IsValid.ShouldBeFalse();
    }

    [Test]
    public void WiredChangesAndAvailabilityNeverMutateEarlierResolvedValues()
    {
        var source = new PortReference("price", "out");
        var graph = new StrategyGraph(
            [Node("price"), Node("wired", "Constant", new Dictionary<string, ParamValue> { ["value"] = new WiredParamValue(source) }, ValueType.Price)],
            Array.Empty<GraphConnection>());
        CompiledTradingNode node = Compile(graph).Nodes.Single(node => node.Node.Id == "wired");
        var resolver = new EngineParameterResolver();
        var outputs = new Dictionary<PortReference, EngineValue> { [source] = EngineValue.Numeric(ValueType.Price, value: 10m) };
        ResolvedEngineParameters first = resolver.Resolve(node, outputs, EngineExecutionInputs.Empty);
        outputs[source] = EngineValue.Numeric(ValueType.Price, value: 20m);
        ResolvedEngineParameters changed = resolver.Resolve(node, outputs, EngineExecutionInputs.Empty);
        outputs.Clear();
        ResolvedEngineParameters missing = resolver.Resolve(node, outputs, EngineExecutionInputs.Empty);
        first["value"].Number.ShouldBe(10m);
        changed["value"].Number.ShouldBe(20m);
        missing["value"].IsAvailable.ShouldBeFalse();
        outputs[source] = EngineValue.Numeric(ValueType.QuoteAmount, value: 20m);
        Should.Throw<InvalidOperationException>(() => resolver.Resolve(node, outputs, EngineExecutionInputs.Empty));
        changed["value"].Number.ShouldBe(20m);
    }

    [Test]
    public void OptimizationSelectionsStayIsolatedWhenOneResolverServesDifferentExecutions()
    {
        var graph = new StrategyGraph(
            [Node("choice", "Constant", new Dictionary<string, ParamValue> { ["value"] = new DecimalOptimizeParamValue(Min: 1m, Max: 3m, Step: 1m) })],
            Array.Empty<GraphConnection>());
        CompiledTradingNode node = Compile(graph).Nodes.Single();
        var reference = new PortReference("choice", "value");
        var firstExecution = new EngineExecutionInputs(new Dictionary<PortReference, decimal> { [reference] = 1m });
        var secondExecution = new EngineExecutionInputs(new Dictionary<PortReference, decimal> { [reference] = 3m });
        var resolver = new EngineParameterResolver();
        var outputs = new Dictionary<PortReference, EngineValue>();
        ResolvedEngineParameters first = resolver.Resolve(node, outputs, firstExecution);
        resolver.Resolve(node, outputs, secondExecution)["value"].Number.ShouldBe(3m);
        resolver.Resolve(node, outputs, firstExecution)["value"].Number.ShouldBe(1m);
        first["value"].Number.ShouldBe(1m);
    }

    [Test]
    public void ChangedUnusedArithmeticStillFailsAndNeverReusesAnEarlierSuccessfulResult()
    {
        var graph = new StrategyGraph(
            [
                new GraphNode("price", "LiveQuote", schemaVersion: 1, new Dictionary<string, ParamValue>()),
                Node("amount", "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(10m) }, ValueType.QuoteAmount),
                Node("divide", "Arithmetic", new Dictionary<string, ParamValue> { ["op"] = Text("Divide") })
            ],
            [Wire("amount", "divide", "a"), Wire("price", "divide", "b")]);
        CompiledTradingGraph compiled = new TradingGraphCompiler().Compile(graph, Definitions.Append(Source("LiveQuote", ValueType.Price, NodeClock.Dispatch)).ToArray())
            .Graph.ShouldNotBeNull();
        var evaluator = new CandleValueEvaluator(compiled);
        var external = new Dictionary<PortReference, EngineValue> { [new PortReference("price", "out")] = EngineValue.Numeric(ValueType.Price, value: 10m) };
        EventOccurrence occurrence = Event(Candle(index: 0, close: 10m));
        evaluator.ReadDispatch(occurrence, external)[new PortReference("divide", "out")].Number.ShouldBe(1m);
        external[new PortReference("price", "out")] = EngineValue.Numeric(ValueType.Price, value: 0m);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var error = Should.Throw<GraphExecutionException>(() => evaluator.ReadDispatch(occurrence, external));
            error.InnerException.ShouldBeOfType<DivideByZeroException>();
        }
    }

    [Test]
    public void PublicDispatchResultsRetainTheirValuesAfterLaterEvaluation()
    {
        var graph = new StrategyGraph([Node("price")], Array.Empty<GraphConnection>());
        var evaluator = new CandleValueEvaluator(Compile(graph));
        var output = new PortReference("price", "out");
        Candle firstCandle = Candle(index: 0, close: 10m);
        evaluator.AdvanceClosedCandle(firstCandle, candleIndex: 0);
        IReadOnlyDictionary<PortReference, EngineValue> first = evaluator.ReadDispatch(Event(firstCandle));
        Candle nextCandle = Candle(index: 1, close: 20m);
        evaluator.AdvanceClosedCandle(nextCandle, candleIndex: 1);
        evaluator.ReadDispatch(Event(nextCandle))[output].Number.ShouldBe(20m);
        first[output].Number.ShouldBe(10m);
    }
}