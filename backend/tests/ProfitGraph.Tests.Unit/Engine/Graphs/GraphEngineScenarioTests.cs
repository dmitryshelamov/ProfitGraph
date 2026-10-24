using System.Text.Json;

using NSubstitute;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Templates;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class GraphEngineScenarioTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void LocallyAcceptedOrderCanBeRejectedAndReleasesItsTagAfterRestore(bool restore)
    {
        (CausalDispatcher dispatcher, Candle candle, DateTimeOffset closeTime, ExecutionSettings settings, InstrumentRules rules) =
            StartLimitGraph(cancelOnSubmitted: false);
        long attemptId = dispatcher.CaptureCheckpoint("v1", closeTime).PendingSubmissionAttempts[1];
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.AcceptLocally(orderId: 1))).ShouldBe([EventKind.Submitted]);
        GraphEngineCheckpoint locallyAccepted = RoundTrip(dispatcher.CaptureCheckpoint("v1", closeTime));
        locallyAccepted.PendingSubmissionAttempts[1].ShouldBe(attemptId);
        locallyAccepted.Account.Progress.Single().LocallyAccepted.ShouldBeTrue();
        locallyAccepted.Account.Progress.Single().Confirmed.ShouldBeFalse();

        if (restore)
        {
            dispatcher = CausalDispatcher.Restore(locallyAccepted, Compile(LimitGraph(cancelOnSubmitted: false)), settings, rules, [candle]);
            dispatcher.Drain();
        }

        DispatchBatch rejected = Update(dispatcher, candle, ExternalOrderUpdate.Reject(orderId: 1, RejectionReason.RiskLimit, "Invented definitive refusal."));
        Kinds(rejected).ShouldBe([EventKind.Rejected]);
        var rejection = rejected.Occurrences.Single().Payload.ShouldBeOfType<RejectionPayload>();
        rejection.AttemptId.ShouldBe(attemptId);
        rejection.Rejection.Reason.ShouldBe(RejectionReason.RiskLimit);
        dispatcher.Account.Capture().QuoteAvailable.ShouldBe(250m);
        dispatcher.Account.Capture().QuoteReserved.ShouldBe(0m);
        dispatcher.Account.Capture().Orders.ShouldBeEmpty();
        dispatcher.CaptureCheckpoint("v1", closeTime).PendingSubmissionAttempts.ShouldBeEmpty();
        dispatcher.Account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 25m, LimitPrice: 5m, "entry", "buy", EligibleFromCandleIndex: 1))
            .Order.ShouldNotBeNull();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RemoteAcceptanceAfterLocalAcceptanceDoesNotRepeatSubmittedAndStillRejectsStaleRefusal(bool restore)
    {
        (CausalDispatcher dispatcher, Candle candle, DateTimeOffset closeTime, ExecutionSettings settings, InstrumentRules rules) =
            StartLimitGraph(cancelOnSubmitted: false);
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.AcceptLocally(orderId: 1))).ShouldBe([EventKind.Submitted]);

        if (restore)
        {
            GraphEngineCheckpoint checkpoint = RoundTrip(dispatcher.CaptureCheckpoint("v1", closeTime));
            dispatcher = CausalDispatcher.Restore(checkpoint, Compile(LimitGraph(cancelOnSubmitted: false)), settings, rules, [candle]);
            dispatcher.Drain();
        }

        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.Accept(orderId: 1))).ShouldBeEmpty();
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.Accept(orderId: 1))).ShouldBeEmpty();
        GraphEngineCheckpoint accepted = dispatcher.CaptureCheckpoint("v1", closeTime);
        accepted.PendingSubmissionAttempts.ShouldBeEmpty();
        accepted.Account.Progress.Single().Confirmed.ShouldBeTrue();
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.Reject(orderId: 1, RejectionReason.RiskLimit, "Invented stale refusal."))).ShouldBeEmpty();
        dispatcher.Account.Capture().QuoteReserved.ShouldBe(25m);
        dispatcher.Account.Capture().Orders.Single().Status.ShouldBe(OrderStatus.Active);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancellationRequestedBeforeLocalAcceptanceSurvivesAcceptanceAndRestore(bool restore)
    {
        (CausalDispatcher dispatcher, Candle candle, DateTimeOffset closeTime, ExecutionSettings settings, InstrumentRules rules) =
            StartLimitGraph(cancelOnSubmitted: false);
        dispatcher.Account.CancelByTag("entry").ShouldNotBeNull().Status.ShouldBe(OrderStatus.PendingCancel);
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.AcceptLocally(orderId: 1))).ShouldBe([EventKind.Submitted]);
        dispatcher.Account.Capture().Orders.Single().Status.ShouldBe(OrderStatus.PendingCancel);

        if (restore)
        {
            GraphEngineCheckpoint checkpoint = RoundTrip(dispatcher.CaptureCheckpoint("v1", closeTime));
            dispatcher = CausalDispatcher.Restore(checkpoint, Compile(LimitGraph(cancelOnSubmitted: false)), settings, rules, [candle]);
            dispatcher.Drain();
        }

        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.Accept(orderId: 1))).ShouldBeEmpty();
        dispatcher.Account.Capture().Orders.Single().Status.ShouldBe(OrderStatus.PendingCancel);
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.ConfirmCancel(orderId: 1))).ShouldBe([EventKind.Cancelled]);
        dispatcher.Account.Capture().Orders.ShouldBeEmpty();
        dispatcher.Account.Capture().QuoteReserved.ShouldBe(0m);
    }

    [Test]
    public void LocalAcceptanceDoesNotAuthorizeRemoteFills()
    {
        (CausalDispatcher dispatcher, Candle candle, DateTimeOffset closeTime, _, _) = StartLimitGraph(cancelOnSubmitted: false);
        Update(dispatcher, candle, ExternalOrderUpdate.AcceptLocally(orderId: 1));
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.Final(orderId: 1, grossBase: 5m, grossQuote: 25m, fee: 0m))).ShouldBeEmpty();
        dispatcher.Account.Capture().BaseTotal.ShouldBe(0m);
        dispatcher.Account.Capture().QuoteReserved.ShouldBe(25m);
        dispatcher.CaptureCheckpoint("v1", closeTime).Account.Progress.Single().Confirmed.ShouldBeFalse();
    }

    [Test]
    public void PendingSubmitRestoresAndAcceptsWithoutResubmission()
    {
        (CausalDispatcher original, Candle candle, DateTimeOffset closeTime, ExecutionSettings settings, InstrumentRules rules) =
            StartLimitGraph(cancelOnSubmitted: false);
        GraphEngineCheckpoint checkpoint = RoundTrip(original.CaptureCheckpoint("v1", closeTime));
        CausalDispatcher restored = CausalDispatcher.Restore(checkpoint, Compile(LimitGraph(cancelOnSubmitted: false)), settings, rules, [candle]);
        Kinds(restored.Drain()).ShouldBe(new[] { EventKind.Resume });
        restored.Account.Capture().Orders.Single().Status.ShouldBe(OrderStatus.PendingSubmit);

        DispatchBatch accepted = Update(restored, candle, ExternalOrderUpdate.Accept(orderId: 1));
        Kinds(accepted).ShouldBe(new[] { EventKind.Submitted });
        accepted.Occurrences[0].Payload.ShouldBeOfType<OrderPayload>().Order.OriginNodeId.ShouldBe("buy");
        restored.Account.Capture().Orders.Single().Id.ShouldBe(1);
        restored.CaptureCheckpoint("v1", closeTime).Account.NextOrderId.ShouldBe(1);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PendingCancelRestoresOriginalCauseAndSettlesTheRace(bool finalFillWins)
    {
        (CausalDispatcher original, Candle candle, DateTimeOffset closeTime, ExecutionSettings settings, InstrumentRules rules) =
            StartLimitGraph(cancelOnSubmitted: true);
        DispatchBatch accepted = Update(original, candle, ExternalOrderUpdate.Accept(orderId: 1));
        long submittedId = accepted.Occurrences.Single().Id;
        original.Account.Capture().Orders.Single().Status.ShouldBe(OrderStatus.PendingCancel);
        GraphEngineCheckpoint checkpoint = RoundTrip(original.CaptureCheckpoint("v1", closeTime));
        CausalDispatcher restored = CausalDispatcher.Restore(checkpoint, Compile(LimitGraph(cancelOnSubmitted: true)), settings, rules, [candle]);
        Kinds(restored.Drain()).ShouldBe(new[] { EventKind.Resume });
        ExternalOrderUpdate terminal = finalFillWins
            ? ExternalOrderUpdate.Final(orderId: 1, grossBase: 5m, grossQuote: 25m, fee: 0m)
            : ExternalOrderUpdate.ConfirmCancel(orderId: 1);

        DispatchBatch settled = Update(restored, candle, terminal);
        Kinds(settled)
            .ShouldBe(
                finalFillWins
                    ? new[] { EventKind.Filled, EventKind.CancelCompleted }
                    : new[] { EventKind.Cancelled, EventKind.CancelCompleted });
        var completion = settled.Occurrences[1].Payload.ShouldBeOfType<CancelResultPayload>();
        completion.Cancelled.ShouldBe(!finalFillWins);
        completion.OriginNodeId.ShouldBe("cancel");
        completion.Cause.Id.ShouldBe(submittedId);
        completion.Cause.Kind.ShouldBe(EventKind.Submitted);
        completion.Cause.Payload.ShouldBe(accepted.Occurrences[0].Payload);
        restored.Account.Capture().Orders.ShouldBeEmpty();
        restored.Account.Capture().QuoteReserved.ShouldBe(0m);
        restored.Account.Capture().BaseTotal.ShouldBe(finalFillWins ? 5m : 0m);
    }

    [Test]
    public void PartialBeforeRestartAndFinalAfterProduceTheSameAggregate()
    {
        (CausalDispatcher original, Candle candle, DateTimeOffset closeTime, ExecutionSettings settings, InstrumentRules rules) =
            StartLimitGraph(cancelOnSubmitted: false);
        Update(original, candle, ExternalOrderUpdate.Accept(orderId: 1));
        Kinds(Update(original, candle, ExternalOrderUpdate.Partial(orderId: 1, grossBase: 2m, grossQuote: 10m, fee: 0.02m))).ShouldBeEmpty();
        GraphEngineCheckpoint checkpoint = RoundTrip(original.CaptureCheckpoint("v1", closeTime));
        ExternalOrderUpdate final = ExternalOrderUpdate.Final(orderId: 1, grossBase: 5m, grossQuote: 25m, fee: 0.05m);
        Fill uninterrupted = Update(original, candle, final).Occurrences.Single().Payload.ShouldBeOfType<FillPayload>().Fill;
        CausalDispatcher restored = CausalDispatcher.Restore(checkpoint, Compile(LimitGraph(cancelOnSubmitted: false)), settings, rules, [candle]);
        Kinds(restored.Drain()).ShouldBe(new[] { EventKind.Resume });
        Fill resumed = Update(restored, candle, final).Occurrences.Single().Payload.ShouldBeOfType<FillPayload>().Fill;
        resumed.ShouldBe(uninterrupted);
        JsonSerializer.Serialize(restored.Account.Capture()).ShouldBe(JsonSerializer.Serialize(original.Account.Capture()));
    }

    [Test]
    public void RestoreWarmsSmaAndAtrAndBlocksCandleEventsUntilReady()
    {
        (ExecutionSettings settings, InstrumentRules rules) = Settings();
        CompiledTradingGraph graph = Compile(IndicatorGraph());
        var original = new CausalDispatcher(graph, new ExternallyExecutedAccount(settings, rules), graphVersionId: "v1");
        Candle[] candles = Enumerable.Range(start: 0, count: 6).Select(index => Candle(index, 5m + index)).ToArray();

        for (var index = 0; index < 5; index++)
        {
            original.AdvanceClosedCandle(candles[index], index);
        }

        DateTimeOffset closeTime = candles[4].OpenTime.AddHours(1);
        GraphEngineCheckpoint checkpoint = RoundTrip(original.CaptureCheckpoint("v1", closeTime));
        CausalDispatcher restored = CausalDispatcher.Restore(checkpoint, graph, settings, rules, candles[..5]);
        restored.AreIndicatorsReady.ShouldBeTrue();
        Kinds(restored.Drain()).ShouldBe(new[] { EventKind.Resume });
        IReadOnlyDictionary<PortReference, EngineValue> expected = original.AdvanceClosedCandle(candles[5], historyIndex: 5);
        IReadOnlyDictionary<PortReference, EngineValue> actual = restored.AdvanceClosedCandle(candles[5], historyIndex: 5);
        actual[new PortReference("sma", "out")].ShouldBe(expected[new PortReference("sma", "out")]);
        actual[new PortReference("atr", "out")].ShouldBe(expected[new PortReference("atr", "out")]);

        CausalDispatcher shortHistory = CausalDispatcher.Restore(checkpoint, graph, settings, rules, [candles[4]]);
        shortHistory.AreIndicatorsReady.ShouldBeFalse();
        Kinds(shortHistory.Drain()).ShouldBe(new[] { EventKind.Resume });
        var close = new ReachedMarketContext(candles[4], candleIndex: 4, closeTime, PricePhase.Close, candles[4].Close, eligibleFromCandleIndex: 5);
        Should.Throw<InvalidOperationException>(() => shortHistory.EnqueueExternal(EventKind.CandleClosed, new CandlePayload(candles[4]), close));
    }

    [Test]
    public void DeletedCancelNodeDropsDelayedCompletionWithDiagnostic()
    {
        (CausalDispatcher dispatcher, Candle candle, DateTimeOffset closeTime, _, _) = StartLimitGraph(cancelOnSubmitted: true);
        Update(dispatcher, candle, ExternalOrderUpdate.Accept(orderId: 1));
        dispatcher.Account.Capture().Orders.Single().Status.ShouldBe(OrderStatus.PendingCancel);
        var replacement = new StrategyGraph(
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

        GraphChangeResult changed = dispatcher.ChangeGraph(replacement, Catalog, EngineExecutionInputs.Empty, "v2", [candle], closeTime);
        changed.Applied.ShouldBeTrue();
        changed.Occurrences.Select(occurrence => occurrence.Kind).ShouldBe(new[] { EventKind.Resume });
        dispatcher.ExternalOrderDiagnostics.Single().Code.ShouldBe("CancelNodeRemoved");
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.ConfirmCancel(orderId: 1))).ShouldBe(new[] { EventKind.Cancelled });
    }

    [Test]
    public void GraphChangeInsideDrainIsRefusedWithoutMutatingEngine()
    {
        (ExecutionSettings settings, InstrumentRules rules) = Settings();
        StrategyGraph graph = LimitGraph(cancelOnSubmitted: false);
        var actual = new TradingAccount(settings, rules);
        var fake = Substitute.For<IGraphAccount>();
        fake.Settings.Returns(settings);
        fake.Rules.Returns(rules);
        fake.Capture().Returns(_ => actual.Capture());
        GraphChangeResult? attempted = null;
        var dispatcher = new CausalDispatcher(Compile(graph), fake, graphVersionId: "v1");
        Candle candle = Candle(index: 0, close: 5m);
        DateTimeOffset closeTime = candle.OpenTime.AddHours(1);
        fake.Submit(Arg.Any<OrderRequest>())
            .Returns(call =>
            {
                attempted = dispatcher.ChangeGraph(graph, Catalog, EngineExecutionInputs.Empty, "v2", [candle], closeTime);

                return actual.Submit(call.Arg<OrderRequest>()!);
            });
        var open = new ReachedMarketContext(candle, candleIndex: 0, closeTime, PricePhase.Open, candle.Open, eligibleFromCandleIndex: 0);
        dispatcher.StartAndDrain(open);
        attempted.ShouldNotBeNull().Applied.ShouldBeFalse();
        dispatcher.GraphVersionId.ShouldBe("v1");
        actual.Orders.Active.Single().OriginNodeId.ShouldBe("buy");
    }

    [Test]
    public void LadderGraphChangeKeepsOpenOrdersAndSmaThenUsesNewDrop()
    {
        (ExecutionSettings settings, InstrumentRules rules) = Settings();
        var dispatcher = new CausalDispatcher(Compile(LadderGraph(drop: 0.1m)), new ExternallyExecutedAccount(settings, rules), graphVersionId: "v1");
        Candle candle = Candle(index: 0, close: 5m);
        DateTimeOffset closeTime = candle.OpenTime.AddHours(1);
        var open = new ReachedMarketContext(candle, candleIndex: 0, closeTime, PricePhase.Open, candle.Open, eligibleFromCandleIndex: 0);
        Kinds(dispatcher.StartAndDrain(open)).ShouldBe(new[] { EventKind.Start });
        dispatcher.AdvanceClosedCandle(candle, historyIndex: 0);
        OrderSnapshot entry = ByTag("ladder.entry");
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.Accept(entry.Id))).ShouldBe(new[] { EventKind.Submitted });
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.Final(entry.Id, grossBase: 5m, grossQuote: 25m, fee: 0m)))
            .ShouldBe(new[] { EventKind.Filled, EventKind.CancelCompleted });
        OrderSnapshot exit = ByTag("ladder.exit");
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.Accept(exit.Id))).ShouldBe(new[] { EventKind.Submitted });
        OrderSnapshot average = ByTag("ladder.average");
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.Accept(average.Id))).ShouldBe(new[] { EventKind.Submitted });
        average.LimitPrice.ShouldBe(4.5m);

        GraphChangeResult changed = dispatcher.ChangeGraph(LadderGraph(drop: 0.2m), Catalog, EngineExecutionInputs.Empty, "v2", [candle], closeTime);
        changed.Applied.ShouldBeTrue();
        changed.KeptIndicatorNodeIds.ShouldContain("sma");
        changed.CancelledOrderIds.ShouldBeEmpty();
        changed.Occurrences.Select(occurrence => occurrence.Kind).ShouldBe(new[] { EventKind.Resume });
        changed.Occurrences.Single().Payload.ShouldBe(new ResumePayload(ResumeReason.GraphChanged, "v1", "v2"));
        dispatcher.Account.Capture().Orders.Select(order => order.Id).ShouldContain(exit.Id);
        dispatcher.Account.Capture().Orders.Select(order => order.Id).ShouldContain(average.Id);
        dispatcher.OrderGraphVersions[exit.Id].ShouldBe("v1");
        dispatcher.OrderGraphVersions[average.Id].ShouldBe("v1");

        decimal averageQuote = average.Quantity!.Value * average.LimitPrice!.Value;
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.Final(average.Id, average.Quantity.Value, averageQuote, fee: 0m)))
            .ShouldBe(new[] { EventKind.Filled });
        ByTag("ladder.exit").Status.ShouldBe(OrderStatus.PendingCancel);
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.ConfirmCancel(exit.Id)))
            .ShouldBe(new[] { EventKind.Cancelled, EventKind.CancelCompleted });
        OrderSnapshot nextExit = ByTag("ladder.exit");
        Kinds(Update(dispatcher, candle, ExternalOrderUpdate.Accept(nextExit.Id))).ShouldBe(new[] { EventKind.Submitted });
        OrderSnapshot nextAverage = ByTag("ladder.average");
        nextAverage.LimitPrice.ShouldBe(3.6m);
        dispatcher.OrderGraphVersions[nextAverage.Id].ShouldBe("v2");

        OrderSnapshot ByTag(string tag)
        {
            return dispatcher.Account.Capture().Orders.Single(order => order.Tag == tag);
        }
    }

    private static (CausalDispatcher Dispatcher, Candle Candle, DateTimeOffset CloseTime, ExecutionSettings Settings, InstrumentRules Rules)
        StartLimitGraph(bool cancelOnSubmitted)
    {
        (ExecutionSettings settings, InstrumentRules rules) = Settings();
        var dispatcher = new CausalDispatcher(Compile(LimitGraph(cancelOnSubmitted)), new ExternallyExecutedAccount(settings, rules), graphVersionId: "v1");
        Candle candle = Candle(index: 0, close: 5m);
        DateTimeOffset closeTime = candle.OpenTime.AddHours(1);
        var open = new ReachedMarketContext(candle, candleIndex: 0, closeTime, PricePhase.Open, candle.Open, eligibleFromCandleIndex: 0);
        Kinds(dispatcher.StartAndDrain(open)).ShouldBe(new[] { EventKind.Start });
        dispatcher.AdvanceClosedCandle(candle, historyIndex: 0);

        return (dispatcher, candle, closeTime, settings, rules);
    }

    private static DispatchBatch Update(CausalDispatcher dispatcher, Candle candle, ExternalOrderUpdate update)
    {
        var context = new ReachedMarketContext(candle, candleIndex: 0, candle.OpenTime.AddHours(1), PricePhase.External, candle.Close, eligibleFromCandleIndex: 1);
        dispatcher.ApplyExternalOrderUpdate(update, context);

        return dispatcher.Drain();
    }

    private static GraphEngineCheckpoint RoundTrip(GraphEngineCheckpoint checkpoint)
    {
        return JsonSerializer.Deserialize<GraphEngineCheckpoint>(JsonSerializer.Serialize(checkpoint)).ShouldNotBeNull();
    }

    private static EventKind[] Kinds(DispatchBatch batch)
    {
        return batch.Occurrences.Select(occurrence => occurrence.Kind).ToArray();
    }

    private static StrategyGraph LimitGraph(bool cancelOnSubmitted)
    {
        var nodes = new List<GraphNode>
        {
            Node("start", "OnStart"), Node("resume", "OnResume"),
            TradingNode(
                "buy",
                "LimitBuy",
                new Dictionary<string, ParamValue>
                {
                    ["amount"] = Number(25m), ["price"] = Number(5m), ["tag"] = Text("entry")
                },
                ActionEvaluationMode.OnEvent)
        };
        var connections = new List<GraphConnection> { Wire("start", "buy", "trigger") };

        if (cancelOnSubmitted)
        {
            nodes.Add(TradingNode("cancel", "CancelOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("entry") }, ActionEvaluationMode.OnEvent));
            connections.Add(new GraphConnection(new PortReference("buy", "submitted"), new PortReference("cancel", "trigger")));
        }

        return new StrategyGraph(nodes, connections);
    }

    private static StrategyGraph IndicatorGraph()
    {
        return new StrategyGraph(
            [
                Node("price"), Node("sma", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(2m) }),
                Node("atr", "ATR", new Dictionary<string, ParamValue> { ["period"] = Number(2m) }), Node("resume", "OnResume")
            ],
            [Wire("price", "sma")]);
    }

    private static StrategyGraph LadderGraph(decimal drop)
    {
        StrategyDefinition definition = LadderStrategyBlockFactory.Create(buyAmount: 25m, allocationCap: 75m, drop, rise: 0.1m);
        StrategyGraph lowered = new StrategyDefinitionLowerer(Catalog).Lower(definition).Graph;

        return new StrategyGraph(
            lowered.Nodes.Concat(
            [
                Node("price"), Node("sma", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(1m) }),
                Node("resume", "OnResume")
            ]),
            lowered.Connections.Concat([Wire("price", "sma")]),
            lowered.TemplateKind);
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