using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class GraphAccountDispatcherTests
{
    [TestCase(arg: false)]
    [TestCase(arg: true)]
    public void OnStartLimitBuyPublishesFakeSubmission(bool replay)
    {
        var order = new Order(
            id: 42,
            "entry",
            OrderSide.Buy,
            OrderType.Limit,
            quantity: 5m,
            limitPrice: 50m,
            quoteBudget: null,
            reservation: 250m,
            "buy",
            eligibleFromCandleIndex: 0);
        var account = new GraphAccountFake(new OrderSubmission(order, Rejection: null));
        DispatchBatch batch = Dispatch(account, replay);
        account.SubmittedRequest.ShouldBe(new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 250m, LimitPrice: 50m, "entry", "buy", EligibleFromCandleIndex: 0));
        batch.Occurrences.Select(occurrence => occurrence.Kind).ShouldBe(new[] { EventKind.Start, EventKind.Submitted });
        EventOccurrence submitted = batch.Occurrences[1];
        submitted.CausationId.ShouldBe(batch.Occurrences[0].Id);
        submitted.Payload.ShouldBeOfType<OrderPayload>().Order.ShouldBe(OrderSnapshot.Capture(order));
        batch.AppliedActions.Single().Results.Single().Kind.ShouldBe(EventKind.Submitted);
    }

    [TestCase(arg: false)]
    [TestCase(arg: true)]
    public void OnStartLimitBuyPublishesFakeRejection(bool replay)
    {
        var rejection = new OrderRejection(RejectionReason.TagActive, "Fake account rejected this tag.", OrderSide.Buy, OrderType.Limit, "entry", "buy", OrderId: null);
        var account = new GraphAccountFake(new OrderSubmission(Order: null, rejection));
        DispatchBatch batch = Dispatch(account, replay);
        account.SubmittedRequest.ShouldNotBeNull().OriginNodeId.ShouldBe("buy");
        batch.Occurrences.Select(occurrence => occurrence.Kind).ShouldBe(new[] { EventKind.Start, EventKind.Rejected });
        EventOccurrence rejected = batch.Occurrences[1];
        rejected.CausationId.ShouldBe(batch.Occurrences[0].Id);
        var payload = rejected.Payload.ShouldBeOfType<RejectionPayload>();
        payload.Rejection.ShouldBeSameAs(rejection);
        payload.Rejection.Reason.ShouldBe(RejectionReason.TagActive);
        payload.Rejection.Message.ShouldBe("Fake account rejected this tag.");
        payload.AttemptId.ShouldBe(1L);
        batch.AppliedActions.Single().Results.Single().Kind.ShouldBe(EventKind.Rejected);
    }

    [TestCase(arg: false)]
    [TestCase(arg: true)]
    public void SubmittedLimitBuyCancelsByTagWithGlobalEventBeforeCompletion(bool replay)
    {
        var order = new Order(
            id: 42,
            "entry",
            OrderSide.Buy,
            OrderType.Limit,
            quantity: 5m,
            limitPrice: 50m,
            quoteBudget: null,
            reservation: 250m,
            "buy",
            eligibleFromCandleIndex: 0);
        var account = new GraphAccountFake(new OrderSubmission(order, Rejection: null));
        DispatchBatch batch = Dispatch(account, replay, cancelOnSubmitted: true);
        batch.Occurrences.Select(occurrence => occurrence.Kind)
            .ShouldBe(new[] { EventKind.Start, EventKind.Submitted, EventKind.Cancelled, EventKind.CancelCompleted });
        account.CancelledTag.ShouldBe("entry");
        order.Status.ShouldBe(OrderStatus.Cancelled);
        batch.Occurrences[2].CausationId.ShouldBe(batch.Occurrences[1].Id);
        batch.Occurrences[3].CausationId.ShouldBe(batch.Occurrences[1].Id);
        batch.Occurrences[2].Payload.ShouldBeOfType<OrderPayload>().Order.ShouldBe(OrderSnapshot.Capture(order));
        var completed = batch.Occurrences[3].Payload.ShouldBeOfType<CancelResultPayload>();
        completed.Cancelled.ShouldBeTrue();
        completed.CancelledOrderId.ShouldBe(order.Id);
        completed.OriginNodeId.ShouldBe("cancel");
        account.Capture().Orders.ShouldBeEmpty();
        account.Capture().QuoteAvailable.ShouldBe(250m);
    }

    private static DispatchBatch Dispatch(IGraphAccount account, bool replay, bool cancelOnSubmitted = false)
    {
        var nodes = new List<GraphNode>
        {
            Node("start", "OnStart"),
            TradingNode("balance", "Balance"),
            TradingNode(
                "buy",
                "LimitBuy",
                new Dictionary<string, ParamValue> { ["price"] = Number(50m), ["tag"] = Text("entry") },
                ActionEvaluationMode.OnEvent)
        };
        var connections = new List<GraphConnection>
        {
            Wire("start", "buy", "trigger"),
            new GraphConnection(new PortReference("balance", "quoteAvailable"), new PortReference("buy", "amount"))
        };

        if (cancelOnSubmitted)
        {
            nodes.Add(TradingNode("cancel", "CancelOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("entry") }, ActionEvaluationMode.OnEvent));
            connections.Add(new GraphConnection(new PortReference("buy", "submitted"), new PortReference("cancel", "trigger")));
        }

        var definition = new StrategyGraph(nodes, connections);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(definition, Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        var dispatcher = new CausalDispatcher(compilation.Graph.ShouldNotBeNull(), account);
        Candle candle = Candle(index: 0, close: 100m);
        var market = new ReachedMarketContext(candle, candleIndex: 0, candle.OpenTime.AddHours(1), PricePhase.Open, candle.Open, eligibleFromCandleIndex: 0);
        dispatcher.BeginOpen(market);
        dispatcher.EnqueueExternal(EventKind.Start, new StartPayload(), market);

        return replay ? dispatcher.DrainForReplay(CancellationToken.None) : dispatcher.Drain();
    }
}