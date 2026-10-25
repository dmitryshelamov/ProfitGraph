using System.Text.Json;
using System.Text.Json.Nodes;

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

public sealed class PositionBarsHeldTests
{
    private static readonly ExecutionSettings Settings = new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);

    private static readonly InstrumentRules Rules = new InstrumentRules(
        quantityStep: 0.01m,
        priceTick: 0.01m,
        minQuantity: 0.01m,
        minNotional: 1m,
        basePrecision: 2,
        quotePrecision: 2);

    [TestCase(OrderType.Market, PricePhase.Open)]
    [TestCase(OrderType.Limit, PricePhase.OpenToLow)]
    public void OpeningCandleCountsOnceAndAveragingAndPartialSellPreserveOpening(OrderType type, PricePhase phase)
    {
        var account = new TradingAccount(Settings, Rules);
        var dispatcher = new CausalDispatcher(Compile(), account);
        dispatcher.BeginOpen(Context(index: 0, PricePhase.Open));
        Close(dispatcher, index: 0).IsAvailable.ShouldBeFalse();
        Order order = Submit(account, OrderSide.Buy, amount: 100m, type, eligibleFrom: 1);
        order.EligibleFromCandleIndex.ShouldBe(1);
        Execute(dispatcher, account, order, index: 1, phase);
        Close(dispatcher, index: 1).Number.ShouldBe(1m);
        Execute(dispatcher, account, Submit(account, OrderSide.Buy, amount: 100m), index: 2, PricePhase.Open);
        Close(dispatcher, index: 2).Number.ShouldBe(2m);
        Execute(dispatcher, account, Submit(account, OrderSide.Sell, amount: 0.5m), index: 3, PricePhase.Open);
        Close(dispatcher, index: 3).Number.ShouldBe(3m);
    }

    [Test]
    public void TimeInAMarketClosureIsNotHeld_AndWithoutClosuresElapsedTimeCounts()
    {
        var closed = new TradingAccount(Settings, Rules);
        var night = new CausalDispatcher(Compile(), closed, closures: [new MarketClosure(DateTimeOffset.UnixEpoch.AddHours(1), DateTimeOffset.UnixEpoch.AddHours(10))]);
        var open = new TradingAccount(Settings, Rules);
        var continuous = new CausalDispatcher(Compile(), open);

        Execute(night, closed, Submit(closed, OrderSide.Buy, amount: 100m), index: 0, PricePhase.Open);
        Execute(continuous, open, Submit(open, OrderSide.Buy, amount: 100m), index: 0, PricePhase.Open);

        Close(night, index: 0).Number.ShouldBe(1m);
        CloseAfterNight(night).Number.ShouldBe(2m);
        Close(continuous, index: 0).Number.ShouldBe(1m);
        CloseAfterNight(continuous).Number.ShouldBe(11m);
    }

    [Test]
    public void SellToDustEndsHoldingPeriodAndNextEntryRestartsAtOne()
    {
        var account = new TradingAccount(Settings, Rules);
        var dispatcher = new CausalDispatcher(Compile(), account);
        Execute(dispatcher, account, Submit(account, OrderSide.Buy, amount: 100m), index: 0, PricePhase.Open);
        Close(dispatcher, index: 0).Number.ShouldBe(1m);
        Execute(dispatcher, account, Submit(account, OrderSide.Sell, amount: 0.995m), index: 1, PricePhase.Open, price: 50m);
        account.Portfolio.BaseTotal.ShouldBe(0.01m);
        Close(dispatcher, index: 1, price: 50m).IsAvailable.ShouldBeFalse();
        Execute(dispatcher, account, Submit(account, OrderSide.Buy, amount: 100m), index: 2, PricePhase.Open, price: 50m);
        Close(dispatcher, index: 2, price: 50m).Number.ShouldBe(1m);
    }

    [Test]
    public void SimulatedExternalCheckpointPersistsOpeningTimeAcrossRestartAndGraphChange()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        var dispatcher = new CausalDispatcher(Compile(), account, graphVersionId: "v1");
        dispatcher.StartAndDrain(Context(index: 0, PricePhase.Open));
        Order order = Submit(account, OrderSide.Buy, amount: 100m);
        dispatcher.ApplySimulatedOrderUpdate(ExternalOrderUpdate.Accept(order.Id), Context(index: 0, PricePhase.Open));
        dispatcher.Drain();
        dispatcher.ApplySimulatedOrderUpdate(ExternalOrderUpdate.Final(order.Id, grossBase: 1m, grossQuote: 100m, fee: 0m), Context(index: 0, PricePhase.OpenToLow));
        dispatcher.Drain();
        Close(dispatcher, index: 0).Number.ShouldBe(1m);
        GraphEngineCheckpoint checkpoint = dispatcher.CaptureCheckpoint("v1", DateTimeOffset.UnixEpoch.AddHours(1));
        checkpoint.PositionOpenedAt.ShouldBe(DateTimeOffset.UnixEpoch);
        checkpoint = JsonSerializer.Deserialize<GraphEngineCheckpoint>(JsonSerializer.Serialize(checkpoint)).ShouldNotBeNull();
        CausalDispatcher restored = CausalDispatcher.Restore(checkpoint, Compile(), Settings, Rules, [Candle(index: 0, close: 100m)]);
        restored.Drain().Observations.Single().Outputs[new PortReference("position", "barsHeld")].Number.ShouldBe(1m);
        restored.BeginOpen(Context(index: 1, PricePhase.Open));
        Close(restored, index: 1).Number.ShouldBe(2m);
        GraphChangeResult changed = restored.ChangeGraph(
            Graph(),
            Catalog,
            EngineExecutionInputs.Empty,
            "v2",
            [Candle(index: 0, close: 100m), Candle(index: 1, close: 100m)],
            DateTimeOffset.UnixEpoch.AddHours(2));
        changed.Applied.ShouldBeTrue();
        changed.Observations.Single().Outputs[new PortReference("position", "barsHeld")].Number.ShouldBe(2m);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LiveFillWithoutExchangeTimestampLeavesHoldingPeriodUnavailable(bool missingTimeOnFirstPartial)
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        var dispatcher = new CausalDispatcher(Compile(), account, graphVersionId: "v1");
        dispatcher.BeginOpen(Context(index: 0, PricePhase.Open));
        Close(dispatcher, index: 0).IsAvailable.ShouldBeFalse();
        Order order = Submit(account, OrderSide.Buy, amount: 100m);
        ReachedMarketContext external = Context(index: 0, PricePhase.External);
        dispatcher.ApplyExternalOrderUpdate(ExternalOrderUpdate.Accept(order.Id), external);
        dispatcher.Drain();

        if (missingTimeOnFirstPartial)
        {
            dispatcher.ApplyExternalOrderUpdate(ExternalOrderUpdate.Partial(order.Id, grossBase: 0.5m, grossQuote: 50m, fee: 0m), external);
            dispatcher.Drain();
        }

        dispatcher.ApplyExternalOrderUpdate(
            ExternalOrderUpdate.Final(order.Id, grossBase: 1m, grossQuote: 100m, fee: 0m),
            external,
            missingTimeOnFirstPartial ? DateTimeOffset.UnixEpoch.AddMinutes(65) : null);
        dispatcher.Drain();
        dispatcher.BeginOpen(Context(index: 1, PricePhase.Open));
        Close(dispatcher, index: 1).IsAvailable.ShouldBeFalse();
        dispatcher.CaptureCheckpoint("v1", DateTimeOffset.UnixEpoch.AddHours(2)).PositionOpenedAt.ShouldBeNull();
    }

    [TestCase(3601, 0, 1, 1)]
    [TestCase(5400, 0, 1, 1)]
    [TestCase(3600, 0, 1, 1)]
    [TestCase(5400, 3, 4, 4)]
    [TestCase(-1, 0, 1, 3)]
    public void LiveOpeningFillUsesItsOwnCandleGrid(int fillSeconds, int lastClosedIndex, int nextClosedIndex, int expectedBars)
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        var dispatcher = new CausalDispatcher(Compile(), account, graphVersionId: "v1");

        for (var index = 0; index <= lastClosedIndex; index++)
        {
            LiveClose(dispatcher, index).IsAvailable.ShouldBeFalse();
        }

        Order order = Submit(account, OrderSide.Buy, amount: 100m);
        DateTimeOffset filledAt = DateTimeOffset.UnixEpoch.AddSeconds(fillSeconds);
        ApplyLive(dispatcher, ExternalOrderUpdate.Accept(order.Id), lastClosedIndex);
        ApplyLive(dispatcher, ExternalOrderUpdate.Final(order.Id, grossBase: 1m, grossQuote: 100m, fee: 0m), lastClosedIndex, filledAt);
        LiveClose(dispatcher, nextClosedIndex).Number.ShouldBe(expectedBars);
        dispatcher.ExternalOrderDiagnostics.ShouldBeEmpty();
        dispatcher.CaptureCheckpoint("v1", Context(nextClosedIndex, PricePhase.Close).CandleCloseTime)
            .PositionOpenedAt.ShouldBe(DateTimeOffset.UnixEpoch.AddHours(fillSeconds < 0 ? -1 : 1));
    }

    [Test]
    public void LivePartialOpeningPersistsAcrossPartialsAveragingAndPartialSaleUntilFlat()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        var dispatcher = new CausalDispatcher(Compile(), account, graphVersionId: "v1");
        LiveClose(dispatcher, index: 0);
        Order firstBuy = Submit(account, OrderSide.Buy, amount: 100m);
        ApplyLive(dispatcher, ExternalOrderUpdate.Accept(firstBuy.Id), lastClosedIndex: 0);
        ApplyLive(
            dispatcher,
            ExternalOrderUpdate.Partial(firstBuy.Id, grossBase: 0.5m, grossQuote: 50m, fee: 0m),
            lastClosedIndex: 0,
            DateTimeOffset.UnixEpoch.AddMinutes(65));
        LiveClose(dispatcher, index: 1).Number.ShouldBe(1m);
        ApplyLive(
            dispatcher,
            ExternalOrderUpdate.Final(firstBuy.Id, grossBase: 1m, grossQuote: 100m, fee: 0m),
            lastClosedIndex: 1,
            DateTimeOffset.UnixEpoch.AddMinutes(125));
        Order averaging = Submit(account, OrderSide.Buy, amount: 100m);
        ApplyLive(dispatcher, ExternalOrderUpdate.Accept(averaging.Id), lastClosedIndex: 1);
        ApplyLive(
            dispatcher,
            ExternalOrderUpdate.Final(averaging.Id, grossBase: 1m, grossQuote: 100m, fee: 0m),
            lastClosedIndex: 1,
            DateTimeOffset.UnixEpoch.AddMinutes(130));
        LiveClose(dispatcher, index: 2).Number.ShouldBe(2m);
        Order partialSell = Submit(account, OrderSide.Sell, amount: 0.5m);
        ApplyLive(dispatcher, ExternalOrderUpdate.Accept(partialSell.Id), lastClosedIndex: 2);
        ApplyLive(dispatcher, ExternalOrderUpdate.Final(partialSell.Id, grossBase: 0.5m, grossQuote: 50m, fee: 0m), lastClosedIndex: 2);
        LiveClose(dispatcher, index: 3).Number.ShouldBe(3m);
        Order flatSell = Submit(account, OrderSide.Sell, amount: 1.5m);
        ApplyLive(dispatcher, ExternalOrderUpdate.Accept(flatSell.Id), lastClosedIndex: 3);
        ApplyLive(dispatcher, ExternalOrderUpdate.Final(flatSell.Id, grossBase: 1.5m, grossQuote: 150m, fee: 0m), lastClosedIndex: 3);
        dispatcher.CaptureCheckpoint("v1", DateTimeOffset.UnixEpoch.AddHours(4)).PositionOpenedAt.ShouldBeNull();
        LiveClose(dispatcher, index: 4).IsAvailable.ShouldBeFalse();
        Order nextBuy = Submit(account, OrderSide.Buy, amount: 100m);
        ApplyLive(dispatcher, ExternalOrderUpdate.Accept(nextBuy.Id), lastClosedIndex: 4);
        ApplyLive(
            dispatcher,
            ExternalOrderUpdate.Final(nextBuy.Id, grossBase: 1m, grossQuote: 100m, fee: 0m),
            lastClosedIndex: 4,
            DateTimeOffset.UnixEpoch.AddMinutes(305));
        LiveClose(dispatcher, index: 5).Number.ShouldBe(1m);
    }

    [Test]
    public void LiveOpeningTimeSurvivesJsonRestoreAndGraphChange()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        var dispatcher = new CausalDispatcher(Compile(), account, graphVersionId: "v1");
        dispatcher.StartAndDrain(Context(index: 0, PricePhase.Open));
        Close(dispatcher, index: 0);
        Order order = Submit(account, OrderSide.Buy, amount: 100m);
        ApplyLive(dispatcher, ExternalOrderUpdate.Accept(order.Id), lastClosedIndex: 0);
        ApplyLive(
            dispatcher,
            ExternalOrderUpdate.Final(order.Id, grossBase: 1m, grossQuote: 100m, fee: 0m),
            lastClosedIndex: 0,
            DateTimeOffset.UnixEpoch.AddMinutes(65));
        LiveClose(dispatcher, index: 1).Number.ShouldBe(1m);
        GraphEngineCheckpoint checkpoint = dispatcher.CaptureCheckpoint("v1", DateTimeOffset.UnixEpoch.AddHours(2));
        checkpoint = JsonSerializer.Deserialize<GraphEngineCheckpoint>(JsonSerializer.Serialize(checkpoint)).ShouldNotBeNull();
        CausalDispatcher restored = CausalDispatcher.Restore(
            checkpoint,
            Compile(),
            Settings,
            Rules,
            [Candle(index: 0, close: 100m), Candle(index: 1, close: 100m)]);
        restored.Drain().Observations.Single().Outputs[new PortReference("position", "barsHeld")].Number.ShouldBe(1m);
        LiveClose(restored, index: 2).Number.ShouldBe(2m);
        GraphChangeResult changed = restored.ChangeGraph(
            Graph(),
            Catalog,
            EngineExecutionInputs.Empty,
            "v2",
            [Candle(index: 0, close: 100m), Candle(index: 1, close: 100m), Candle(index: 2, close: 100m)],
            DateTimeOffset.UnixEpoch.AddHours(3));
        changed.Applied.ShouldBeTrue();
        changed.Observations.Single().Outputs[new PortReference("position", "barsHeld")].Number.ShouldBe(2m);
    }

    [Test]
    public void RawCheckpointWithoutOpeningTimeRestoresHeldPositionUnchanged()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        var dispatcher = new CausalDispatcher(Compile(), account, graphVersionId: "v1");
        dispatcher.StartAndDrain(Context(index: 0, PricePhase.Open));
        Close(dispatcher, index: 0);
        Order order = Submit(account, OrderSide.Buy, amount: 100m);
        ApplyLive(dispatcher, ExternalOrderUpdate.Accept(order.Id), lastClosedIndex: 0);
        ApplyLive(dispatcher, ExternalOrderUpdate.Final(order.Id, grossBase: 1m, grossQuote: 100m, fee: 0m), lastClosedIndex: 0);
        GraphEngineCheckpoint checkpoint = dispatcher.CaptureCheckpoint("v1", DateTimeOffset.UnixEpoch.AddHours(1));
        JsonObject json = JsonNode.Parse(JsonSerializer.Serialize(checkpoint)).ShouldNotBeNull().AsObject();
        json.Remove("PositionOpenedAt").ShouldBeTrue();
        GraphEngineCheckpoint legacy = json.Deserialize<GraphEngineCheckpoint>().ShouldNotBeNull();
        legacy.PositionOpenedAt.ShouldBeNull();
        CausalDispatcher restored = CausalDispatcher.Restore(legacy, Compile(), Settings, Rules, [Candle(index: 0, close: 100m)]);
        restored.Drain().Observations.Single().Outputs[new PortReference("position", "barsHeld")].IsAvailable.ShouldBeFalse();
        restored.Account.Capture().BaseTotal.ShouldBe(1m);
        restored.Account.Capture().CostBasis.ShouldBe(100m);
        LiveClose(restored, index: 1).IsAvailable.ShouldBeFalse();
    }

    [Test]
    public void FutureExecutionTimeIsDiagnosedWithoutClampingItsCandle()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        var dispatcher = new CausalDispatcher(Compile(), account, graphVersionId: "v1");
        LiveClose(dispatcher, index: 0);
        Order order = Submit(account, OrderSide.Buy, amount: 100m);
        ApplyLive(dispatcher, ExternalOrderUpdate.Accept(order.Id), lastClosedIndex: 0);
        ApplyLive(
            dispatcher,
            ExternalOrderUpdate.Final(order.Id, grossBase: 1m, grossQuote: 100m, fee: 0m),
            lastClosedIndex: 0,
            DateTimeOffset.UnixEpoch.AddMinutes(185));
        dispatcher.ExternalOrderDiagnostics.Single().Code.ShouldBe("ExternalFillExecutionTimeInFuture");
        dispatcher.CaptureCheckpoint("v1", DateTimeOffset.UnixEpoch.AddHours(1)).PositionOpenedAt.ShouldBe(DateTimeOffset.UnixEpoch.AddHours(3));
        LiveClose(dispatcher, index: 1).IsAvailable.ShouldBeFalse();
        LiveClose(dispatcher, index: 2);
        LiveClose(dispatcher, index: 3).Number.ShouldBe(1m);
    }

    private static EngineValue LiveClose(CausalDispatcher dispatcher, int index)
    {
        dispatcher.BeginOpen(Context(index, PricePhase.Open));

        return Close(dispatcher, index);
    }

    private static void ApplyLive(CausalDispatcher dispatcher, ExternalOrderUpdate update, int lastClosedIndex, DateTimeOffset? executionTime = null)
    {
        dispatcher.ApplyExternalOrderUpdate(update, Context(lastClosedIndex, PricePhase.External), executionTime);
        dispatcher.Drain();
    }

    [Test]
    public void HoldingPeriodCanBeComparedWithAnIntegerParameter()
    {
        var graph = new StrategyGraph(
            [
                TradingNode("position", "Position"), Node("hold", "Parameter", new Dictionary<string, ParamValue> { ["value"] = Number(18m) }, ValueType.Integer),
                Node("exit", "Compare", new Dictionary<string, ParamValue> { ["op"] = Text(">=") })
            ],
            [new GraphConnection(new PortReference("position", "barsHeld"), new PortReference("exit", "a")), Wire("hold", "exit", "b")]);
        TradingGraphCompilationResult result = new TradingGraphCompiler().Compile(graph, Catalog);
        result.Validation.Diagnostics.ShouldBeEmpty();
        result.Graph.ShouldNotBeNull().Nodes.Single(node => node.Node.Id == "position").OutputTypes["barsHeld"].ShouldBe(ValueType.Integer);
    }

    private static StrategyGraph Graph()
    {
        return new StrategyGraph(
            [TradingNode("position", "Position"), Node("clock", "OnCandleClosed"), Node("resume", "OnResume"), Node("heldAvailable", "IsAvailable")],
            [new GraphConnection(new PortReference("position", "barsHeld"), new PortReference("heldAvailable", "in"))]);
    }

    private static CompiledTradingGraph Compile()
    {
        TradingGraphCompilationResult result = new TradingGraphCompiler().Compile(Graph(), Catalog);
        result.Validation.Diagnostics.ShouldBeEmpty();

        return result.Graph.ShouldNotBeNull();
    }

    private static Order Submit(IGraphAccount account, OrderSide side, decimal amount, OrderType type = OrderType.Market, int eligibleFrom = 0)
    {
        return account.Submit(new OrderRequest(side, type, amount, type == OrderType.Limit ? 100m : null, Tag: null, "manual", eligibleFrom)).Order.ShouldNotBeNull();
    }

    private static ReachedMarketContext Context(int index, PricePhase phase, decimal price = 100m)
    {
        return new ReachedMarketContext(
            Candle(index, price),
            index,
            DateTimeOffset.UnixEpoch.AddHours(index + 1),
            phase,
            price,
            phase is PricePhase.Close or PricePhase.External ? index + 1 : index);
    }

    private static EngineValue Close(CausalDispatcher dispatcher, int index, decimal price = 100m)
    {
        ReachedMarketContext context = Context(index, PricePhase.Close, price);
        dispatcher.AdvanceClosedCandle(context.Candle, index);
        dispatcher.BeginClose(context);
        dispatcher.EnqueueExternal(EventKind.CandleClosed, new CandlePayload(context.Candle), context);

        return dispatcher.Drain().Observations.Single().Outputs[new PortReference("position", "barsHeld")];
    }

    /// <summary>
    ///     Closes the next candle, which opens at hour 10, after a night without candles.
    /// </summary>
    private static EngineValue CloseAfterNight(CausalDispatcher dispatcher)
    {
        DateTimeOffset opens = DateTimeOffset.UnixEpoch.AddHours(10);
        var candle = new Candle(opens, Open: 100m, High: 100m, Low: 100m, Close: 100m, Volume: 1m);
        var context = new ReachedMarketContext(candle, candleIndex: 1, opens.AddHours(1), PricePhase.Close, marketPrice: 100m, eligibleFromCandleIndex: 2);
        dispatcher.BeginOpen(new ReachedMarketContext(candle, candleIndex: 1, opens.AddHours(1), PricePhase.Open, marketPrice: 100m, eligibleFromCandleIndex: 1));
        dispatcher.AdvanceClosedCandle(context.Candle, historyIndex: 1);
        dispatcher.BeginClose(context);
        dispatcher.EnqueueExternal(EventKind.CandleClosed, new CandlePayload(context.Candle), context);

        return dispatcher.Drain().Observations.Single().Outputs[new PortReference("position", "barsHeld")];
    }

    private static void Execute(CausalDispatcher dispatcher, TradingAccount account, Order order, int index, PricePhase phase, decimal price = 100m)
    {
        ReachedMarketContext context = Context(index, phase, price);
        dispatcher.BeginOpen(Context(index, PricePhase.Open, price));
        OrderSnapshot snapshot = OrderSnapshot.Capture(order);
        dispatcher.PublishExecution(snapshot, account.Execute(order.Id, price), context);
        dispatcher.Drain();
    }
}