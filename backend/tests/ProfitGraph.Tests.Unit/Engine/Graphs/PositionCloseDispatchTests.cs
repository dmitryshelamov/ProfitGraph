using System.Text.Json;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

/// <summary>
///     The owner's position close (ADR 0064): it cancels the bot's working orders, rejects every other order locally while it
///     lasts, survives a checkpoint, and sells what the position holds once nothing works.
/// </summary>
public sealed class PositionCloseDispatchTests
{
    private static readonly ExecutionSettings Settings = new ExecutionSettings(startingCapital: 250m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);

    private static readonly InstrumentRules Rules = new InstrumentRules(
        quantityStep: 0.01m,
        priceTick: 0.01m,
        minQuantity: 0.01m,
        minNotional: 1m,
        basePrecision: 2,
        quotePrecision: 2);

    private readonly Candle _candle = Candle(index: 0, close: 5m);
    private readonly Guid _request = Guid.NewGuid();

    [Test]
    public void ACloseCancelsTheWorkingOrdersAndRejectsOthersUntilItEnds()
    {
        (CausalDispatcher dispatcher, ExternallyExecutedAccount account, _) = Holding();

        dispatcher.BeginPositionClose(_request).ShouldBe(1);
        dispatcher.Drain();

        dispatcher.PositionClose.ShouldBe(new PositionCloseState(_request));
        account.Capture().Orders.ShouldHaveSingleItem().Status.ShouldBe(OrderStatus.PendingCancel);
        dispatcher.SubmitManualOrder(OrderSide.Buy, amount: 10m, limitPrice: 3m, External())
            .Rejection.ShouldNotBeNull()
            .Reason.ShouldBe(RejectionReason.PositionClosing);

        Apply(dispatcher, ExternalOrderUpdate.ConfirmCancel(orderId: 2));
        dispatcher.EndPositionClose();

        dispatcher.PositionClose.ShouldBeNull();
        dispatcher.SubmitManualOrder(OrderSide.Sell, amount: 1m, limitPrice: 9m, External()).Order.ShouldNotBeNull();
    }

    [Test]
    public void ARestoredCloseSellsThePositionOnceTheOrdersAreGone()
    {
        (CausalDispatcher dispatcher, _, CompiledTradingGraph graph) = Holding();
        dispatcher.BeginPositionClose(_request);
        dispatcher.Drain();
        GraphEngineCheckpoint checkpoint = JsonSerializer.Deserialize<GraphEngineCheckpoint>(
                JsonSerializer.Serialize(dispatcher.CaptureCheckpoint("v1", _candle.OpenTime.AddHours(1))))
            .ShouldNotBeNull();

        checkpoint.PositionClose.ShouldBe(new PositionCloseState(_request));
        CausalDispatcher restored = CausalDispatcher.Restore(checkpoint, graph, Settings, Rules, [_candle]);
        restored.Drain();
        restored.SubmitManualOrder(OrderSide.Buy, amount: 10m, limitPrice: 3m, External())
            .Rejection.ShouldNotBeNull()
            .Reason.ShouldBe(RejectionReason.PositionClosing);
        restored.ApplyExternalOrderUpdate(ExternalOrderUpdate.ConfirmCancel(orderId: 2), External());
        restored.Drain();

        Order sell = restored.SubmitPositionCloseOrder(External()).Order.ShouldNotBeNull();
        restored.Drain();

        sell.Side.ShouldBe(OrderSide.Sell);
        sell.Type.ShouldBe(OrderType.Market);
        sell.Tag.ShouldBe(PositionCloseOrder.Tag);
        sell.OriginNodeId.ShouldBe(PositionCloseOrder.OriginNodeId);
        restored.Account.Capture().Orders.ShouldHaveSingleItem().Quantity.ShouldBe(6.25m);
        restored.PositionClose.ShouldBe(new PositionCloseState(_request, sell.Id));
    }

    private (CausalDispatcher Dispatcher, ExternallyExecutedAccount Account, CompiledTradingGraph Graph) Holding()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        var graph = new StrategyGraph(
            [
                Node("start", "OnStart"),
                TradingNode(
                    "entry",
                    "LimitBuy",
                    new Dictionary<string, ParamValue> { ["amount"] = Number(25m), ["price"] = Number(4m), ["tag"] = Text("entry") },
                    ActionEvaluationMode.OnEvent)
            ],
            [Wire("start", "entry", "trigger")]);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(graph, Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        CompiledTradingGraph compiled = compilation.Graph.ShouldNotBeNull();
        var dispatcher = new CausalDispatcher(compiled, account, graphVersionId: "v1");
        var open = new ReachedMarketContext(_candle, candleIndex: 0, _candle.OpenTime.AddHours(1), PricePhase.Open, _candle.Open, eligibleFromCandleIndex: 0);
        dispatcher.StartAndDrain(open);
        dispatcher.AdvanceClosedCandle(_candle, historyIndex: 0);
        Apply(dispatcher, ExternalOrderUpdate.Accept(orderId: 1));
        Apply(dispatcher, ExternalOrderUpdate.Final(orderId: 1, grossBase: 6.25m, grossQuote: 25m, fee: 0m));
        Order exit = dispatcher.SubmitManualOrder(OrderSide.Sell, amount: 6.25m, limitPrice: 6m, External()).Order.ShouldNotBeNull();
        dispatcher.Drain();
        Apply(dispatcher, ExternalOrderUpdate.Accept(exit.Id));

        return (dispatcher, account, compiled);
    }

    private void Apply(CausalDispatcher dispatcher, ExternalOrderUpdate update)
    {
        dispatcher.ApplyExternalOrderUpdate(update, External());
        dispatcher.Drain();
    }

    private ReachedMarketContext External()
    {
        return new ReachedMarketContext(_candle, candleIndex: 0, _candle.OpenTime.AddHours(1), PricePhase.External, marketPrice: 5m, eligibleFromCandleIndex: 1);
    }
}