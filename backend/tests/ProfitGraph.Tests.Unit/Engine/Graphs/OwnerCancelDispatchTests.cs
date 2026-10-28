using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

/// <summary>
///     The owner's cancel of one working order (ADR 0048): the order awaits cancellation as when the strategy cancels it, and the
///     venue's confirmation ends it without a cancel completion, because no node asked.
/// </summary>
public sealed class OwnerCancelDispatchTests
{
    private readonly Candle _candle = Candle(index: 0, close: 5m);

    [Test]
    public void AWorkingOrderAwaitsCancellationWithoutAnEvent()
    {
        (CausalDispatcher dispatcher, ExternallyExecutedAccount account) = Started();

        dispatcher.RequestOrderCancel(orderId: 1).ShouldBe(ExternalCancelDisposition.Pending);

        dispatcher.Drain().Occurrences.ShouldBeEmpty();
        account.Capture().Orders.ShouldHaveSingleItem().Status.ShouldBe(OrderStatus.PendingCancel);
        account.Capture().QuoteReserved.ShouldBe(25m);
    }

    [Test]
    public void TheVenuesConfirmationEndsItAsCancelledWithoutACompletion()
    {
        (CausalDispatcher dispatcher, ExternallyExecutedAccount account) = Started();
        dispatcher.RequestOrderCancel(orderId: 1);

        DispatchBatch cancelled = Apply(dispatcher, ExternalOrderUpdate.ConfirmCancel(orderId: 1));

        cancelled.Occurrences.Select(occurrence => occurrence.Kind).ShouldBe([EventKind.Cancelled]);
        account.Capture().Orders.ShouldBeEmpty();
        account.Capture().QuoteReserved.ShouldBe(0m);
    }

    [Test]
    public void AnOrderAlreadyAwaitingCancellationIsReported()
    {
        (CausalDispatcher dispatcher, _) = Started();
        dispatcher.RequestOrderCancel(orderId: 1);

        dispatcher.RequestOrderCancel(orderId: 1).ShouldBe(ExternalCancelDisposition.AlreadyPending);
    }

    [Test]
    public void AnOrderThatIsNotWorkingHasNoTarget()
    {
        (CausalDispatcher dispatcher, ExternallyExecutedAccount account) = Started();
        Apply(dispatcher, ExternalOrderUpdate.Final(orderId: 1, grossBase: 6.25m, grossQuote: 25m, fee: 0m));

        dispatcher.RequestOrderCancel(orderId: 1).ShouldBe(ExternalCancelDisposition.NoTarget);
        dispatcher.RequestOrderCancel(orderId: 99).ShouldBe(ExternalCancelDisposition.NoTarget);
        account.Capture().Orders.ShouldBeEmpty();
    }

    [Test]
    public void AManualOrderStillBeingSubmittedCanBeCancelled()
    {
        (CausalDispatcher dispatcher, ExternallyExecutedAccount account) = Started();
        Order manual = dispatcher.SubmitManualOrder(OrderSide.Buy, amount: 10m, limitPrice: 3m, External()).Order.ShouldNotBeNull();
        dispatcher.Drain();

        dispatcher.RequestOrderCancel(manual.Id).ShouldBe(ExternalCancelDisposition.Pending);

        account.Capture().Orders.Single(order => order.Id == manual.Id).Status.ShouldBe(OrderStatus.PendingCancel);
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
                    ActionEvaluationMode.OnEvent)
            ],
            [Wire("start", "entry", "trigger")]);
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