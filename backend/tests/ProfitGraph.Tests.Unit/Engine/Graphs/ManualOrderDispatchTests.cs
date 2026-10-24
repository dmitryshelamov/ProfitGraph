using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class ManualOrderDispatchTests
{
    private readonly Candle _candle = Candle(index: 0, close: 5m);

    [Test]
    public void ManualBuyFillUpdatesAccountAndReachesOnlyTaggedGlobalListener()
    {
        (CausalDispatcher dispatcher, ExternallyExecutedAccount account) = Started();

        OrderSubmission submission = dispatcher.SubmitManualOrder(OrderSide.Buy, amount: 50m, External());
        dispatcher.Drain();

        Order order = submission.Order.ShouldNotBeNull();
        order.Status.ShouldBe(OrderStatus.PendingSubmit);
        order.Type.ShouldBe(OrderType.Market);
        order.Tag.ShouldBe(ManualOrder.Tag);
        order.OriginNodeId.ShouldBe(ManualOrder.OriginNodeId);
        account.Capture().QuoteReserved.ShouldBe(75m);

        Apply(dispatcher, ExternalOrderUpdate.Accept(order.Id));
        DispatchBatch filled = Apply(dispatcher, ExternalOrderUpdate.Final(order.Id, grossBase: 10m, grossQuote: 50m, fee: 0.1m));

        FillPayload fill = filled.Occurrences.Select(occurrence => occurrence.Payload).OfType<FillPayload>().ShouldHaveSingleItem();
        fill.Fill.Tag.ShouldBe(ManualOrder.Tag);
        fill.OriginNodeId.ShouldBe(ManualOrder.OriginNodeId);
        AccountSnapshot snapshot = account.Capture();
        snapshot.BaseTotal.ShouldBe(9.9m);
        snapshot.QuoteTotal.ShouldBe(200m);
        snapshot.Orders.ShouldHaveSingleItem().Status.ShouldBe(OrderStatus.PendingCancel);
        snapshot.Orders.Single().Tag.ShouldBe("entry");
    }

    [Test]
    public void ManualSellUsesOnlyFreeBaseAndOneManualOrderIsActive()
    {
        (CausalDispatcher dispatcher, ExternallyExecutedAccount account) = Started();
        Order buy = dispatcher.SubmitManualOrder(OrderSide.Buy, amount: 50m, External()).Order.ShouldNotBeNull();
        dispatcher.Drain();
        Apply(dispatcher, ExternalOrderUpdate.Accept(buy.Id));
        Apply(dispatcher, ExternalOrderUpdate.Final(buy.Id, grossBase: 10m, grossQuote: 50m, fee: 0.1m));

        OrderSubmission tooLarge = dispatcher.SubmitManualOrder(OrderSide.Sell, amount: 10m, External());

        tooLarge.Order.ShouldBeNull();
        tooLarge.Rejection.ShouldNotBeNull().Reason.ShouldBe(RejectionReason.InsufficientBase);
        dispatcher.Drain().Occurrences.ShouldBeEmpty();

        dispatcher.SubmitManualOrder(OrderSide.Sell, amount: 5m, External()).Order.ShouldNotBeNull();
        dispatcher.Drain();
        account.Capture().BaseReserved.ShouldBe(5m);

        dispatcher.SubmitManualOrder(OrderSide.Sell, amount: 1m, External()).Rejection.ShouldNotBeNull().Reason.ShouldBe(RejectionReason.TagActive);
    }

    [Test]
    public void GraphChangeKeepsActiveManualOrder()
    {
        (CausalDispatcher dispatcher, ExternallyExecutedAccount account) = Started();
        Order manual = dispatcher.SubmitManualOrder(OrderSide.Buy, amount: 50m, External()).Order.ShouldNotBeNull();
        dispatcher.Drain();
        var next = new StrategyGraph([Node("resume", "OnResume")], []);

        GraphChangeResult result = dispatcher.ChangeGraph(next, Catalog, EngineExecutionInputs.Empty, "v2", [_candle], _candle.OpenTime.AddHours(1));

        result.Applied.ShouldBeTrue();
        result.CancelledOrderIds.ShouldNotContain(manual.Id);
        account.Capture().Orders.Single(order => order.Id == manual.Id).Status.ShouldBe(OrderStatus.PendingSubmit);
    }

    private (CausalDispatcher Dispatcher, ExternallyExecutedAccount Account) Started()
    {
        var settings = new ExecutionSettings(startingCapital: 250m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);
        var rules = new InstrumentRules(quantityStep: 0.01m, priceTick: 0.01m, minQuantity: 0.01m, minNotional: 1m, basePrecision: 2, quotePrecision: 2);
        var account = new ExternallyExecutedAccount(settings, rules);
        var graph = new StrategyGraph(
            [
                Node("start", "OnStart"),
                TradingNode(
                    "entry",
                    "LimitBuy",
                    new Dictionary<string, ParamValue> { ["amount"] = Number(25m), ["price"] = Number(4m), ["tag"] = Text("entry") },
                    ActionEvaluationMode.OnEvent),
                TradingNode("manualFilled", "OnOrderFilled", new Dictionary<string, ParamValue> { ["tag"] = Text(ManualOrder.Tag) }),
                TradingNode("cancelEntry", "CancelOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("entry") }, ActionEvaluationMode.OnEvent)
            ],
            [Wire("start", "entry", "trigger"), Wire("manualFilled", "cancelEntry", "trigger")]);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(graph, Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        var dispatcher = new CausalDispatcher(compilation.Graph.ShouldNotBeNull(), account, graphVersionId: "v1");
        var open = new ReachedMarketContext(_candle, candleIndex: 0, _candle.OpenTime.AddHours(1), PricePhase.Open, _candle.Open, eligibleFromCandleIndex: 0);
        dispatcher.StartAndDrain(open);
        dispatcher.AdvanceClosedCandle(_candle, historyIndex: 0);
        Apply(dispatcher, ExternalOrderUpdate.Accept(orderId: 1));

        return (dispatcher, account);
    }

    private DispatchBatch Apply(CausalDispatcher dispatcher, ExternalOrderUpdate update)
    {
        dispatcher.ApplyExternalOrderUpdate(update, External());

        return dispatcher.Drain();
    }

    private ReachedMarketContext External()
    {
        return new ReachedMarketContext(_candle, candleIndex: 0, _candle.OpenTime.AddHours(1), PricePhase.External, marketPrice: 5m, eligibleFromCandleIndex: 1);
    }
}