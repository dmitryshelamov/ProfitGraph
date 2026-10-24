using System.Text.Json;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class GraphEngineCheckpointTests
{
    [Test]
    public void RoundTrippedCheckpointRestoresPendingOrderAndResumesWithoutStart()
    {
        CompiledTradingGraph graph = Graph();
        (ExecutionSettings settings, InstrumentRules rules) = Settings();
        var account = new ExternallyExecutedAccount(settings, rules);
        var dispatcher = new CausalDispatcher(graph, account, graphVersionId: "v1");
        Candle candle = Candle(index: 0, close: 5m);
        DateTimeOffset closeTime = candle.OpenTime.AddHours(1);
        var open = new ReachedMarketContext(candle, candleIndex: 0, closeTime, PricePhase.Open, candle.Open, eligibleFromCandleIndex: 0);
        dispatcher.StartAndDrain(open).Occurrences.Select(occurrence => occurrence.Kind).ShouldBe(new[] { EventKind.Start });
        dispatcher.AdvanceClosedCandle(candle, historyIndex: 0);
        GraphEngineCheckpoint captured = dispatcher.CaptureCheckpoint("v1", closeTime);
        captured.Account.Orders.Single().Status.ShouldBe(OrderStatus.PendingSubmit);
        string json = JsonSerializer.Serialize(captured);
        GraphEngineCheckpoint copy = JsonSerializer.Deserialize<GraphEngineCheckpoint>(json).ShouldNotBeNull();
        CausalDispatcher restored = CausalDispatcher.Restore(copy, graph, settings, rules, [candle]);
        restored.Started.ShouldBeTrue();
        restored.Drain().Occurrences.Select(occurrence => occurrence.Kind).ShouldBe(new[] { EventKind.Resume });
        restored.StartAndDrain(open).Occurrences.ShouldNotContain(occurrence => occurrence.Kind == EventKind.Start);
        restored.CaptureCheckpoint("v1", closeTime).Account.Orders.Single().Id.ShouldBe(captured.Account.Orders.Single().Id);
    }

    [Test]
    public void CaptureRefusesQueuedWorkAndRestoreRefusesWrongLastCandle()
    {
        CompiledTradingGraph graph = Graph();
        (ExecutionSettings settings, InstrumentRules rules) = Settings();
        var dispatcher = new CausalDispatcher(graph, new ExternallyExecutedAccount(settings, rules), graphVersionId: "v1");
        Candle candle = Candle(index: 0, close: 5m);
        DateTimeOffset closeTime = candle.OpenTime.AddHours(1);
        dispatcher.AdvanceClosedCandle(candle, historyIndex: 0);
        GraphEngineCheckpoint checkpoint = dispatcher.CaptureCheckpoint("v1", closeTime);
        var context = new ReachedMarketContext(candle, candleIndex: 0, closeTime, PricePhase.Close, candle.Close, eligibleFromCandleIndex: 1);
        dispatcher.EnqueueExternal(EventKind.CandleClosed, new CandlePayload(candle), context);
        Should.Throw<InvalidOperationException>(() => dispatcher.CaptureCheckpoint("v1", closeTime));
        Should.Throw<ArgumentException>(() => CausalDispatcher.Restore(checkpoint, graph, settings, rules, [Candle(index: 1, close: 5m)]));
    }

    private static CompiledTradingGraph Graph()
    {
        var graph = new StrategyGraph(
            [
                Node("start", "OnStart"), Node("resume", "OnResume"), TradingNode(
                    "buy",
                    "LimitBuy",
                    new Dictionary<string, ParamValue>
                    {
                        ["amount"] = Number(25m), ["price"] = Number(5m), ["tag"] = Text("entry")
                    },
                    ActionEvaluationMode.OnEvent)
            ],
            [Wire("start", "buy", "trigger")]);

        return new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldNotBeNull();
    }

    private static (ExecutionSettings Settings, InstrumentRules Rules) Settings()
    {
        return (
            new ExecutionSettings(startingCapital: 250m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.01m, priceTick: 0.01m, minQuantity: 0.01m, minNotional: 1m, basePrecision: 2, quotePrecision: 2));
    }
}