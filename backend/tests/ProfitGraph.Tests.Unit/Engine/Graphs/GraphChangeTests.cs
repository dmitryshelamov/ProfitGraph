using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class GraphChangeTests
{
    [Test]
    public void DeletedOrderNodeCancelsAndRoutesOnlyToGlobalListenerBeforeResume()
    {
        (ExecutionSettings settings, InstrumentRules rules) = Settings();
        var account = new TradingAccount(settings, rules);
        var dispatcher = new CausalDispatcher(Compile(OrderGraph()), account, graphVersionId: "v1");
        Candle candle = Candle(index: 0, close: 5m);
        DateTimeOffset closeTime = candle.OpenTime.AddHours(1);
        var open = new ReachedMarketContext(candle, candleIndex: 0, closeTime, PricePhase.Open, candle.Open, eligibleFromCandleIndex: 0);
        dispatcher.StartAndDrain(open);
        long orderId = account.Orders.Active.Single().Id;
        dispatcher.AdvanceClosedCandle(candle, historyIndex: 0);
        var next = new StrategyGraph([Node("resume", "OnResume"), TradingNode("cancelled", "OnOrderCancelled")], []);

        GraphChangeResult result = dispatcher.ChangeGraph(next, Catalog, EngineExecutionInputs.Empty, "v2", [candle], closeTime);

        result.Applied.ShouldBeTrue();
        result.CancelledOrderIds.ShouldBe(new[] { orderId });
        result.Occurrences.Select(occurrence => occurrence.Kind).ShouldBe(new[] { EventKind.Cancelled, EventKind.Resume });
        result.Observations[0].Outputs[new PortReference("cancelled", "out")].IsAvailable.ShouldBeTrue();
        result.Observations[1].Outputs[new PortReference("resume", "out")].IsAvailable.ShouldBeTrue();
        account.Orders.Active.ShouldBeEmpty();
        account.Portfolio.BaseTotal.ShouldBe(0m);
        dispatcher.OrderGraphVersions[orderId].ShouldBe("v1");
    }

    [Test]
    public void IndicatorIdentityKeepsUnchangedStateAndWarmsChangedPeriod()
    {
        (ExecutionSettings settings, InstrumentRules rules) = Settings();
        var dispatcher = new CausalDispatcher(Compile(IndicatorGraph(smaPeriod: 2)), new ExternallyExecutedAccount(settings, rules), graphVersionId: "v1");
        Candle[] candles = Enumerable.Range(start: 0, count: 5).Select(index => Candle(index, 5m + index)).ToArray();

        for (var index = 0; index < candles.Length; index++)
        {
            dispatcher.AdvanceClosedCandle(candles[index], index);
        }

        DateTimeOffset closeTime = candles[^1].OpenTime.AddHours(1);
        GraphChangeResult unchanged = dispatcher.ChangeGraph(IndicatorGraph(smaPeriod: 2), Catalog, EngineExecutionInputs.Empty, "v2", candles, closeTime);
        unchanged.Applied.ShouldBeTrue();
        unchanged.KeptIndicatorNodeIds.ShouldContain("sma");
        unchanged.KeptIndicatorNodeIds.ShouldContain("atr");
        GraphChangeResult changed = dispatcher.ChangeGraph(IndicatorGraph(smaPeriod: 3), Catalog, EngineExecutionInputs.Empty, "v3", candles, closeTime);
        changed.Applied.ShouldBeTrue();
        changed.KeptIndicatorNodeIds.ShouldNotContain("sma");
        changed.KeptIndicatorNodeIds.ShouldContain("atr");
        changed.Occurrences.Single().Payload.ShouldBe(new ResumePayload(ResumeReason.GraphChanged, "v2", "v3"));

        Candle next = Candle(index: 5, close: 10m);
        EngineValue actual = dispatcher.AdvanceClosedCandle(next, historyIndex: 5)[new PortReference("sma", "out")];
        var fresh = new CandleValueEvaluator(Compile(IndicatorGraph(smaPeriod: 3)));

        for (var index = 0; index < candles.Length; index++)
        {
            fresh.AdvanceClosedCandle(candles[index], index);
        }

        EngineValue expected = fresh.AdvanceClosedCandle(next, candleIndex: 5)[new PortReference("sma", "out")];
        actual.ShouldBe(expected);
    }

    [Test]
    public void InvalidGraphAndInputsLeaveVersionAndAccountUntouched()
    {
        (ExecutionSettings settings, InstrumentRules rules) = Settings();
        var account = new TradingAccount(settings, rules);
        var dispatcher = new CausalDispatcher(Compile(OrderGraph()), account, graphVersionId: "v1");
        Candle candle = Candle(index: 0, close: 5m);
        dispatcher.AdvanceClosedCandle(candle, historyIndex: 0);
        DateTimeOffset closeTime = candle.OpenTime.AddHours(1);
        var invalid = new StrategyGraph([Node("same", "OnStart"), Node("same", "OnResume")], []);
        dispatcher.ChangeGraph(invalid, Catalog, EngineExecutionInputs.Empty, "v2", [candle], closeTime).Applied.ShouldBeFalse();
        dispatcher.GraphVersionId.ShouldBe("v1");
        var wrongInputs = new EngineExecutionInputs(new Dictionary<PortReference, decimal> { [new PortReference("missing", "period")] = 2m });
        dispatcher.ChangeGraph(OrderGraph(), Catalog, wrongInputs, "v2", [candle], closeTime).Applied.ShouldBeFalse();
        dispatcher.GraphVersionId.ShouldBe("v1");
        account.Orders.Active.ShouldBeEmpty();
    }

    private static StrategyGraph OrderGraph()
    {
        return new StrategyGraph(
            [
                Node("start", "OnStart"), TradingNode(
                    "buy",
                    "LimitBuy",
                    new Dictionary<string, ParamValue>
                    {
                        ["amount"] = Number(25m), ["price"] = Number(5m), ["tag"] = Text("entry")
                    },
                    ActionEvaluationMode.OnEvent)
            ],
            [Wire("start", "buy", "trigger")]);
    }

    private static StrategyGraph IndicatorGraph(int smaPeriod)
    {
        return new StrategyGraph(
            [
                Node("price"), Node("sma", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(smaPeriod) }),
                Node("atr", "ATR", new Dictionary<string, ParamValue> { ["period"] = Number(2m) }), Node("resume", "OnResume")
            ],
            [Wire("price", "sma")]);
    }

    private static CompiledTradingGraph Compile(StrategyGraph graph)
    {
        return new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldNotBeNull();
    }

    private static (ExecutionSettings Settings, InstrumentRules Rules) Settings()
    {
        return (
            new ExecutionSettings(startingCapital: 250m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.01m, priceTick: 0.01m, minQuantity: 0.01m, minNotional: 1m, basePrecision: 2, quotePrecision: 2));
    }
}