using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class CausalDispatcherTests
{
    private static TradingAccount Account(decimal capital = 1000m)
    {
        return new TradingAccount(
            new ExecutionSettings(capital, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.01m, minQuantity: 0m, minNotional: 0m, basePrecision: 6, quotePrecision: 2));
    }

    private static CompiledTradingGraph Plan(StrategyGraph graph)
    {
        TradingGraphCompilationResult result = new TradingGraphCompiler().Compile(graph, Catalog);
        result.Validation.Diagnostics.ShouldBeEmpty();

        return result.Graph.ShouldNotBeNull();
    }

    private static ReachedMarketContext Context(PricePhase phase = PricePhase.Open, int index = 0)
    {
        return new ReachedMarketContext(
            Candle(index, close: 100m),
            index,
            DateTimeOffset.UnixEpoch.AddHours(index + 1),
            phase,
            marketPrice: 100m,
            phase == PricePhase.Close ? index + 1 : index);
    }

    private static GraphNode Buy(string id, string tag, ParamValue? amount = null, ActionEvaluationMode mode = ActionEvaluationMode.OnEvent)
    {
        var parameters = new Dictionary<string, ParamValue> { ["tag"] = Text(tag) };

        if (amount is not null)
        {
            parameters["amount"] = amount;
        }

        return TradingNode(id, "MarketBuy", parameters, mode);
    }

    private static DispatchBatch Drain(CausalDispatcher dispatcher, bool replay)
    {
        return replay
            ? dispatcher.DrainForReplay(CancellationToken.None)
            : dispatcher.Drain();
    }

    [TestCase(arg: false)]
    [TestCase(arg: true)]
    public void OverflowRetainsSuccessfulMutationAndCurrentOccurrenceContext(bool preview)
    {
        const decimal tinyPrice = 0.0000000000000000000000000001m;
        GraphNode failing = preview
            ? TradingNode("z", "OrderSizePreview", new Dictionary<string, ParamValue> { ["type"] = Text("Limit"), ["price"] = Number(tinyPrice) })
            : TradingNode(
                "z",
                "LimitBuy",
                new Dictionary<string, ParamValue> { ["amount"] = Number(100m), ["price"] = Number(tinyPrice), ["tag"] = Text("tiny") },
                ActionEvaluationMode.OnEvent);
        var nodes = new List<GraphNode> { Node("start", "OnStart"), Buy("a", "first", Number(100m)), failing };
        var connections = new List<GraphConnection> { Wire("start", "a", "trigger") };

        if (preview)
        {
            nodes.Add(TradingNode("budget", "EventFields", new Dictionary<string, ParamValue> { ["field"] = Text("Order.ReservedQuote") }));
            connections.Add(new GraphConnection(new PortReference("a", "submitted"), new PortReference("budget", "event")));
            connections.Add(new GraphConnection(new PortReference("budget", "out"), new PortReference("z", "amount")));
        }
        else
        {
            connections.Add(Wire("start", "z", "trigger"));
        }

        CompiledTradingGraph graph = Plan(new StrategyGraph(nodes, connections));
        var account = new TradingAccount(
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(tinyPrice, tinyPrice, minQuantity: 0m, minNotional: 0m, basePrecision: 28, quotePrecision: 2));
        var dispatcher = new CausalDispatcher(graph, account);
        ReachedMarketContext context = Context(index: 7);
        dispatcher.BeginOpen(context);
        dispatcher.EnqueueExternal(EventKind.Start, new StartPayload(), context);
        var error = Should.Throw<GraphExecutionException>(() => dispatcher.Drain());
        error.NodeId.ShouldBe("z");
        error.BarIndex.ShouldBe(7);
        error.OccurrenceId.ShouldBe(preview ? 2L : 1L);
        error.InnerException.ShouldBeOfType<OverflowException>();
        account.Orders.Active.Single().OriginNodeId.ShouldBe("a");
        account.Portfolio.QuoteReserved.ShouldBe(100m);
        account.Portfolio.QuoteAvailable.ShouldBe(900m);
        dispatcher.LastBatch.AppliedActions.Single().NodeId.ShouldBe("a");
        dispatcher.LastBatch.AppliedActions.Single().Results.Single().Kind.ShouldBe(EventKind.Submitted);
        dispatcher.LastBatch.Observations[0].Occurrence.Kind.ShouldBe(EventKind.Start);
    }

    [Test]
    public void PureDispatchFailureUsesReplayOccurrenceInsteadOfHistoryOrdinal()
    {
        CompiledTradingGraph graph = Plan(
            new StrategyGraph(
                [
                    TradingNode("balance", "Balance"), Node("zero", "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(0m) }, ValueType.Scalar),
                    Node("divide", "Arithmetic", new Dictionary<string, ParamValue> { ["op"] = Text("Divide") })
                ],
                [new GraphConnection(new PortReference("balance", "quoteTotal"), new PortReference("divide", "a")), Wire("zero", "divide", "b")]));
        var evaluator = new CandleValueEvaluator(graph);
        evaluator.AdvanceClosedCandle(Candle(index: 0, close: 100m), candleIndex: 0);
        evaluator.AdvanceClosedCandle(Candle(index: 1, close: 100m), candleIndex: 1);
        TradingAccount account = Account();
        ReachedMarketContext market = Context(index: 7);
        var context = new DispatchEvaluationContext(AccountSnapshot.Capture(account), account.Rules, account.Settings, market);
        EventOccurrence occurrence = Event(market.Candle, close: false, replayIndex: 7);
        var error = Should.Throw<GraphExecutionException>(() => evaluator.ReadDispatch(occurrence, dispatch: context));
        error.NodeId.ShouldBe("divide");
        error.BarIndex.ShouldBe(7);
        error.OccurrenceId.ShouldBe(occurrence.Id);
        error.InnerException.ShouldBeOfType<DivideByZeroException>();
    }

    [Test]
    public void SiblingActionsReadOneSnapshotAndApplySequentiallyWithoutRollback()
    {
        CompiledTradingGraph graph = Plan(
            new StrategyGraph(
                [Node("start", "OnStart"), TradingNode("balance", "Balance"), Buy("b", "second"), Buy("a", "first")],
                [
                    Wire("start", "a", "trigger"), Wire("start", "b", "trigger"),
                    new GraphConnection(new PortReference("balance", "quoteAvailable"), new PortReference("a", "amount")),
                    new GraphConnection(new PortReference("balance", "quoteAvailable"), new PortReference("b", "amount"))
                ]));
        TradingAccount account = Account();
        var dispatcher = new CausalDispatcher(graph, account);
        ReachedMarketContext context = Context();
        dispatcher.BeginOpen(context);
        dispatcher.EnqueueExternal(EventKind.Start, new StartPayload(), context);
        DispatchBatch batch = dispatcher.Drain();
        DispatchObservation initial = batch.Observations[0];
        initial.Requests.Select(request => ((SubmitActionRequest)request).Order.Amount).ShouldBe(new[] { 1000m, 1000m });
        initial.Requests.Select(request => request.NodeId).ShouldBe(new[] { "a", "b" });
        batch.Observations.Select(observation => observation.Occurrence.Kind).ShouldBe(new[] { EventKind.Start, EventKind.Submitted, EventKind.Rejected });
        batch.Observations[1].Account.QuoteAvailable.ShouldBe(0m);
        account.Orders.Active.Single().OriginNodeId.ShouldBe("a");
        account.Portfolio.QuoteReserved.ShouldBe(1000m);
        ((RejectionPayload)batch.Observations[2].Occurrence.Payload).Rejection.Reason.ShouldBe(RejectionReason.InsufficientQuote);
    }

    [TestCase(arg: false)]
    [TestCase(arg: true)]
    public void DeferredFilledFeedbackSeedsBeforeItsOwnerAndDirectGlobalMergeDeduplicates(bool replay)
    {
        var graph = new StrategyGraph(
            [
                Node("start", "OnStart"), Buy("owner", "entry"), TradingNode("global", "OnOrderFilled"), Node("merge", "MergeEvents"),
                TradingNode("field", "EventFields", new Dictionary<string, ParamValue> { ["field"] = Text("Fill.QuoteChange") }),
                Node("negative", "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(-1m) }),
                Node("amount", "Arithmetic", new Dictionary<string, ParamValue> { ["op"] = Text("Multiply") }),
                TradingNode("cancel", "CancelOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("absent") }, ActionEvaluationMode.OnEvent)
            ],
            [
                Wire("start", "owner", "trigger"),
                new GraphConnection(new PortReference("owner", "filled"), new PortReference("merge", "a")), Wire("global", "merge", "b"),
                new GraphConnection(new PortReference("owner", "filled"), new PortReference("field", "event")),
                Wire("field", "amount", "a"), Wire("negative", "amount", "b"), Wire("amount", "owner", "amount"), Wire("merge", "cancel", "trigger")
            ]);
        CompiledTradingGraph compiled = Plan(graph);
        compiled.Nodes.Single(node => node.Node.Id == "field").DependencyRank.ShouldBeLessThan(compiled.Nodes.Single(node => node.Node.Id == "owner").DependencyRank);
        TradingAccount account = Account();
        Order order = account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Market, Amount: 100m, LimitPrice: null, "entry", "owner", EligibleFromCandleIndex: 0))
            .Order.ShouldNotBeNull();
        OrderSnapshot original = OrderSnapshot.Capture(order);
        OrderExecution execution = account.Execute(order.Id, referencePrice: 100m);
        var dispatcher = new CausalDispatcher(compiled, account);
        ReachedMarketContext context = Context();
        dispatcher.BeginOpen(context);
        dispatcher.PublishExecution(original, execution, context);
        DispatchBatch batch = Drain(dispatcher, replay);
        batch.AppliedActions.Count.ShouldBe(1);
        batch.AppliedActions[0].NodeId.ShouldBe("cancel");
        IReadOnlyList<EventOccurrence> occurrences = batch.Occurrences;
        occurrences.Select(item => item.Kind).ShouldBe(new[] { EventKind.Filled, EventKind.CancelCompleted });

        if (!replay)
        {
            DispatchObservation fill = batch.Observations[0];
            fill.Outputs[new PortReference("field", "out")].Number.ShouldBe(-100m);
            fill.Outputs[new PortReference("amount", "out")].Number.ShouldBe(100m);
            fill.Outputs[new PortReference("owner", "filled")].Event.Id.ShouldBe(fill.Outputs[new PortReference("global", "out")].Event.Id);
            DispatchObservation completed = batch.Observations[1];
            completed.Outputs[new PortReference("owner", "filled")].IsAvailable.ShouldBeFalse();
            completed.Outputs[new PortReference("global", "out")].IsAvailable.ShouldBeFalse();
            completed.Outputs[new PortReference("field", "out")].IsAvailable.ShouldBeFalse();
            ((CancelResultPayload)completed.Occurrence.Payload).Cancelled.ShouldBeFalse();
        }
        else
        {
            batch.Observations.ShouldBeEmpty();
            ((CancelResultPayload)occurrences[1].Payload).Cancelled.ShouldBeFalse();
        }
    }

    [Test]
    public void ActualCancellationReleasesBeforeGlobalCancellationAndCompletedFreshState()
    {
        CompiledTradingGraph graph = Plan(
            new StrategyGraph(
                [
                    Node("start", "OnStart"),
                    TradingNode("cancel", "CancelOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("held") }, ActionEvaluationMode.OnEvent),
                    TradingNode("balance", "Balance"), TradingNode("global", "OnOrderCancelled")
                ],
                [Wire("start", "cancel", "trigger")]));
        TradingAccount account = Account();
        account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 100m, LimitPrice: 90m, "held", "oldOwner", EligibleFromCandleIndex: 0))
            .Order.ShouldNotBeNull();
        var dispatcher = new CausalDispatcher(graph, account);
        ReachedMarketContext context = Context();
        dispatcher.BeginOpen(context);
        dispatcher.EnqueueExternal(EventKind.Start, new StartPayload(), context);
        DispatchBatch batch = dispatcher.Drain();
        batch.Observations.Select(item => item.Occurrence.Kind).ShouldBe(new[] { EventKind.Start, EventKind.Cancelled, EventKind.CancelCompleted });
        batch.Observations[1].Account.QuoteReserved.ShouldBe(0m);
        batch.Observations[1].Outputs[new PortReference("global", "out")].IsAvailable.ShouldBeTrue();
        batch.Observations[2].Account.QuoteAvailable.ShouldBe(1000m);
        ((CancelResultPayload)batch.Observations[2].Occurrence.Payload).Cancelled.ShouldBeTrue();
    }

    [Test]
    public void CloseContinuationKeepsNextCandleEligibilityAndNeverFillsOnSubmit()
    {
        CompiledTradingGraph graph = Plan(new StrategyGraph([Buy("buy", "entry", Number(100m), ActionEvaluationMode.OnCandleClose)], []));
        TradingAccount account = Account();
        var dispatcher = new CausalDispatcher(graph, account);
        dispatcher.BeginOpen(Context());
        dispatcher.AdvanceClosedCandle(Candle(index: 0, close: 100m), historyIndex: 0);
        ReachedMarketContext close = Context(PricePhase.Close);
        dispatcher.BeginClose(close);
        dispatcher.EnqueueExternal(EventKind.CandleClosed, new CandlePayload(close.Candle), close);
        DispatchBatch batch = dispatcher.Drain();
        batch.Observations.Select(item => item.Occurrence.Kind).ShouldBe(new[] { EventKind.CandleClosed, EventKind.Submitted });
        account.Orders.Active.Single().EligibleFromCandleIndex.ShouldBe(1);
        account.Portfolio.BaseTotal.ShouldBe(0m);
    }

    [Test]
    public void GuardFailureAfterAcceptanceRetainsCommittedFactsAndReservations()
    {
        CompiledTradingGraph graph = Plan(new StrategyGraph([Node("start", "OnStart"), Buy("buy", "entry", Number(10m))], [Wire("start", "buy", "trigger")]));
        TradingAccount account = Account(capital: 200000m);
        var dispatcher = new CausalDispatcher(graph, account);
        ReachedMarketContext context = Context();
        dispatcher.BeginOpen(context);

        for (var index = 0; index < CascadeGuard.Limit - 1; index++)
        {
            Order order = account
                .Submit(new OrderRequest(OrderSide.Buy, OrderType.Market, Amount: 10m, LimitPrice: null, Tag: null, "outside", EligibleFromCandleIndex: 0))
                .Order.ShouldNotBeNull();
            OrderSnapshot original = OrderSnapshot.Capture(order);
            dispatcher.PublishExecution(original, account.Execute(order.Id, referencePrice: 100m), context);
        }

        dispatcher.Drain();
        dispatcher.EnqueueExternal(EventKind.Start, new StartPayload(), context);
        var failure = Should.Throw<CascadeLimitException>(() => dispatcher.Drain());
        failure.Count.ShouldBe(10001);
        dispatcher.LastBatch.AppliedActions.Single().NodeId.ShouldBe("buy");
        LifecycleEmission committed = dispatcher.LastBatch.AppliedActions.Single().Results.Single();
        committed.Kind.ShouldBe(EventKind.Submitted);
        ((OrderPayload)committed.Payload).Order.Tag.ShouldBe("entry");
        account.Portfolio.QuoteReserved.ShouldBe(10m);
        (account.Portfolio.Equity(100m) - account.Portfolio.StartingCapital).ShouldBe(account.Portfolio.RealizedPnl + account.Portfolio.UnrealizedPnl(100m));
        dispatcher.LastPublishedExecution.ShouldNotBeNull().Result.Fill.ShouldNotBeNull();
    }

    [Test]
    public void LogicalRootsResetFlatPricesButRelabelingDoesNotResetCascade()
    {
        var guard = new CascadeGuard();
        guard.BeginOpen(candleIndex: 0, price: 100m);

        for (var index = 0; index < CascadeGuard.Limit; index++)
        {
            guard.Consume("node", causationId: 1);
        }

        guard.AdvancePrice(price: 100m);
        Should.Throw<CascadeLimitException>(() => guard.Consume("node", causationId: 1)).NodeId.ShouldBe("node");
        guard.BeginClose(candleIndex: 0, price: 100m);
        guard.Consume("node", causationId: 1);
        guard.BeginOpen(candleIndex: 1, price: 100m);
        guard.Consume("node", causationId: 1);
        Should.Throw<ArgumentException>(() => guard.BeginOpen(candleIndex: 1, price: 100m));
    }

    [Test]
    public void CloseCancellationCompletionReadsFreshBalanceAndRetainsNextOpenEligibility()
    {
        CompiledTradingGraph graph = Plan(
            new StrategyGraph(
                [
                    Node("close", "OnCandleClosed"),
                    TradingNode("cancel", "CancelOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("held") }, ActionEvaluationMode.OnEvent),
                    TradingNode("balance", "Balance"), Buy("replace", "replacement")
                ],
                [
                    Wire("close", "cancel", "trigger"),
                    new GraphConnection(new PortReference("cancel", "completed"), new PortReference("replace", "trigger")),
                    new GraphConnection(new PortReference("balance", "quoteAvailable"), new PortReference("replace", "amount"))
                ]));
        TradingAccount account = Account();
        account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 100m, LimitPrice: 90m, "held", "original", EligibleFromCandleIndex: 0))
            .IsAccepted.ShouldBeTrue();
        var dispatcher = new CausalDispatcher(graph, account);
        dispatcher.BeginOpen(Context());
        dispatcher.AdvanceClosedCandle(Candle(index: 0, close: 100m), historyIndex: 0);
        ReachedMarketContext close = Context(PricePhase.Close);
        dispatcher.BeginClose(close);
        dispatcher.EnqueueExternal(EventKind.CandleClosed, new CandlePayload(close.Candle), close);
        DispatchBatch batch = dispatcher.Drain();
        Order replacement = account.Orders.Active.Single();
        replacement.Tag.ShouldBe("replacement");
        replacement.Reservation.ShouldBe(1000m);
        replacement.EligibleFromCandleIndex.ShouldBe(1);
        batch.Observations.Single(item => item.Occurrence.Kind == EventKind.CancelCompleted).Requests.Single().NodeId.ShouldBe("replace");
        account.Portfolio.BaseTotal.ShouldBe(0m);
    }

    [Test]
    public void UnavailableRequiredValueAndCancelTargetSkipWithoutFabricatedResults()
    {
        CompiledTradingGraph graph = Plan(
            new StrategyGraph(
                [
                    Node("start", "OnStart"), TradingNode("position", "Position"),
                    TradingNode("active", "ActiveOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("absent") }),
                    TradingNode(
                        "buy",
                        "LimitBuy",
                        new Dictionary<string, ParamValue> { ["amount"] = Number(10m), ["tag"] = Text("entry") },
                        ActionEvaluationMode.OnEvent),
                    TradingNode("cancel", "CancelOrder", mode: ActionEvaluationMode.OnEvent)
                ],
                [
                    Wire("start", "buy", "trigger"), Wire("start", "cancel", "trigger"),
                    new GraphConnection(new PortReference("position", "averageCost"), new PortReference("buy", "price")),
                    new GraphConnection(new PortReference("active", "orderId"), new PortReference("cancel", "orderId"))
                ]));
        TradingAccount account = Account();
        var dispatcher = new CausalDispatcher(graph, account);
        ReachedMarketContext context = Context();
        dispatcher.BeginOpen(context);
        dispatcher.EnqueueExternal(EventKind.Start, new StartPayload(), context);
        DispatchBatch batch = dispatcher.Drain();
        batch.AppliedActions.ShouldBeEmpty();
        batch.Observations.Count.ShouldBe(1);
        batch.Skips.Select(skip => skip.Reason).ShouldBe(new[] { "RequiredValueUnavailable", "CancelTargetUnavailable" }, ignoreOrder: true);
        account.Portfolio.QuoteReserved.ShouldBe(0m);
    }

    [Test]
    public void DisabledActionSkipsWithoutSubmissionAndCancelFactoryProvesExclusiveTarget()
    {
        GraphNode buy = Buy("buy", "entry", Number(100m));
        var parameters = new Dictionary<string, ParamValue>(buy.Parameters) { ["enabled"] = new FixedParamValue(new BooleanParamLiteral(false)) };
        CompiledTradingGraph graph = Plan(
            new StrategyGraph(
                [Node("start", "OnStart"), new GraphNode(buy.Id, buy.TypeKey, buy.SchemaVersion, parameters, evaluationMode: buy.EvaluationMode)],
                [Wire("start", "buy", "trigger")]));
        TradingAccount account = Account();
        var dispatcher = new CausalDispatcher(graph, account);
        ReachedMarketContext context = Context();
        dispatcher.BeginOpen(context);
        dispatcher.EnqueueExternal(EventKind.Start, new StartPayload(), context);
        DispatchBatch batch = dispatcher.Drain();
        batch.AppliedActions.ShouldBeEmpty();
        batch.Skips.Single().Reason.ShouldBe("Disabled");
        batch.Observations.Count.ShouldBe(1);
        CancelActionRequest.ByOrderId("cancel", orderId: 1).Tag.ShouldBeNull();
        CancelActionRequest.ByTag("cancel", "entry").OrderId.ShouldBeNull();
        Should.Throw<ArgumentOutOfRangeException>(() => CancelActionRequest.ByOrderId("cancel", orderId: 0));
        Should.Throw<ArgumentException>(() => CancelActionRequest.ByTag("cancel", " "));
    }

    [Test]
    public void ReusedTagCannotRouteOldFillToTheNewOwner()
    {
        CompiledTradingGraph graph = Plan(
            new StrategyGraph(
                [
                    Buy("old", "shared", Number(10m), ActionEvaluationMode.OnCandleClose),
                    Buy("new", "shared", Number(10m), ActionEvaluationMode.OnCandleClose),
                    TradingNode("global", "OnOrderFilled", new Dictionary<string, ParamValue> { ["tag"] = Text("shared"), ["side"] = Text("Buy") })
                ],
                []));
        TradingAccount account = Account();
        Order old = account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Market, Amount: 10m, LimitPrice: null, "shared", "old", EligibleFromCandleIndex: 0))
            .Order.ShouldNotBeNull();
        OrderSnapshot original = OrderSnapshot.Capture(old);
        OrderExecution execution = account.Execute(old.Id, referencePrice: 100m);
        account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Market, Amount: 10m, LimitPrice: null, "shared", "new", EligibleFromCandleIndex: 0))
            .IsAccepted.ShouldBeTrue();
        var dispatcher = new CausalDispatcher(graph, account);
        ReachedMarketContext context = Context();
        dispatcher.BeginOpen(context);
        dispatcher.PublishExecution(original, execution, context);
        DispatchObservation observation = dispatcher.Drain().Observations.Single();
        observation.Outputs[new PortReference("old", "filled")].IsAvailable.ShouldBeTrue();
        observation.Outputs[new PortReference("new", "filled")].IsAvailable.ShouldBeFalse();
        observation.Outputs[new PortReference("global", "out")].Event.Id.ShouldBe(observation.Outputs[new PortReference("old", "filled")].Event.Id);
    }
}