using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class ActionCollectorTests
{
    [TestCase("event-match")]
    [TestCase("event-other")]
    [TestCase("event-unavailable")]
    [TestCase("event-disabled")]
    [TestCase("close")]
    [TestCase("close-open")]
    [TestCase("close-start")]
    public void SnapshotAndReplayPreserveActionEligibilityAndWiredParameters(string scenario)
    {
        bool eventMode = scenario.StartsWith("event-", StringComparison.Ordinal);
        GraphNode amount = Node("amount", "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(25m) }, ValueType.QuoteAmount);
        GraphNode action = TradingNode(
            "buy",
            "MarketBuy",
            new Dictionary<string, ParamValue>
            {
                ["amount"] = new WiredParamValue(new PortReference("amount", "out")),
                ["tag"] = Text("entry"),
                ["enabled"] = new FixedParamValue(new BooleanParamLiteral(scenario != "event-disabled"))
            },
            eventMode ? ActionEvaluationMode.OnEvent : ActionEvaluationMode.OnCandleClose);
        var source = new PortReference("start", "out");
        var graph = new StrategyGraph(
            [Node("start", "OnStart"), action, amount],
            eventMode ? [new GraphConnection(source, new PortReference("buy", "trigger"))] : []);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(graph, Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        CompiledTradingGraph compiled = compilation.Graph.ShouldNotBeNull();
        var evaluator = new CandleValueEvaluator(compiled);
        var outputs = new Dictionary<PortReference, EngineValue>(evaluator.ReadStaticValues());
        Candle candle = Candle(index: 0, close: 100m);
        PricePhase phase = scenario == "close-open" ? PricePhase.Open : PricePhase.Close;
        EventKind kind = scenario == "close-start" ? EventKind.Start : EventKind.CandleClosed;
        var occurrence = new EventOccurrence(
            id: 2,
            sequence: 2,
            causationId: null,
            kind,
            candleIndex: 0,
            candle.OpenTime,
            candle.OpenTime.AddHours(1),
            phase,
            marketPrice: 100m,
            kind == EventKind.Start ? new StartPayload() : new CandlePayload(candle));
        var context = new ReachedMarketContext(candle, candleIndex: 0, candle.OpenTime.AddHours(1), phase, marketPrice: 100m, eligibleFromCandleIndex: 1);

        if (eventMode && scenario != "event-unavailable")
        {
            EventOccurrence trigger = scenario == "event-other"
                ? new EventOccurrence(
                    id: 1,
                    sequence: 1,
                    causationId: null,
                    kind,
                    candleIndex: 0,
                    candle.OpenTime,
                    candle.OpenTime.AddHours(1),
                    phase,
                    marketPrice: 100m,
                    occurrence.Payload)
                : occurrence;
            outputs[source] = EngineValue.FromEvent(trigger);
        }

        var slots = new EngineValue[compiled.SlotCount];

        foreach ((PortReference reference, EngineValue value) in outputs)
        {
            slots[compiled.SlotOf(reference)] = value;
        }

        var collector = new ActionCollector();
        ActionCollectionResult snapshot = collector.Collect(compiled, occurrence, context, outputs, EngineExecutionInputs.Empty);
        var requests = new List<ActionRequest>();
        var skips = new List<ActionSkipDiagnostic>();
        collector.CollectForReplay(compiled, occurrence, context, slots, evaluator, requests, skips);
        requests.ShouldBe(snapshot.Requests);
        skips.ShouldBe(snapshot.Skips);

        if (scenario is "event-match" or "close")
        {
            var request = requests.Single().ShouldBeOfType<SubmitActionRequest>();
            request.Order.Amount.ShouldBe(25m);
            request.Order.Tag.ShouldBe("entry");
            request.Order.EligibleFromCandleIndex.ShouldBe(1);
            skips.ShouldBeEmpty();
        }
        else
        {
            requests.ShouldBeEmpty();

            if (scenario == "event-disabled")
            {
                skips.Single().Reason.ShouldBe("Disabled");
            }
            else
            {
                skips.ShouldBeEmpty();
            }
        }
    }
}