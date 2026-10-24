using NSubstitute;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Engine.Fixtures;
using ProfitGraph.Tests.Unit.Engine.Graphs;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class TemplateReplayTests
{
    private static readonly IReadOnlyList<INodeDefinition> Definitions = EngineGraphFixture.Definitions
        .Concat(Enum.GetValues<TradingNodeBehavior>().Select(behavior => new TradingNodeDefinition(behavior)))
        .ToArray();

    [TestCase(false)]
    [TestCase(true)]
    public void CompositeTemplates_PreserveFlatTemplateLedgerAndPnl(bool ladder)
    {
        StrategyGraph composite = new StrategyDefinitionLowerer(ProductionTradingNodeTests.Catalog)
            .Lower(ladder ? LadderStrategyBlockFactory.Create() : SmaStrategyBlockFactory.Create(fastPeriod: 2, slowPeriod: 3))
            .Graph;
        StrategyGraph legacy = ladder ? RawLadderStrategyGraphFactory.Create() : RawSmaStrategyGraphFactory.Create(fastPeriod: 2, slowPeriod: 3);
        Candle[] candles = ladder
            ? new[]
            {
                Candle(index: 0, open: 100m, low: 100m, high: 100m, close: 100m), Candle(index: 1, open: 100m, low: 89m, high: 106m, close: 100m),
                Candle(index: 2, open: 100m, low: 100m, high: 112m, close: 110m), Candle(index: 3, open: 110m, low: 99m, high: 120m, close: 115m)
            }
            : SmaCandles();

        foreach (decimal fee in new[] { 0m, 0.001m })
        {
            BacktestResult expected = Run(legacy, candles, fee);
            StrategyGraph previousComposite = ladder
                ? RawCompositeLadderStrategyGraphFactory.Create()
                : RawCompositeSmaStrategyGraphFactory.Create(fastPeriod: 2, slowPeriod: 3);
            BacktestResult previous = Run(previousComposite, candles, fee);
            previous.Ledger.ShouldBe(expected.Ledger);
            previous.Terminal.TotalPnl.ShouldBe(expected.Terminal.TotalPnl);
            BacktestResult actual = Run(composite, candles, fee);
            actual.Terminal.Outcome.ShouldBe(expected.Terminal.Outcome);
            actual.Terminal.TotalPnl.ShouldBe(expected.Terminal.TotalPnl);
            actual.Ledger.ShouldBe(expected.Ledger);
            actual.FillCount.ShouldBe(expected.FillCount);
            actual.OrderCount.ShouldBe(expected.OrderCount);
        }
    }

    [TestCase(arg1: false, arg2: false)]
    [TestCase(arg1: false, arg2: true)]
    [TestCase(arg1: true, arg2: false)]
    [TestCase(arg1: true, arg2: true)]
    public void Sma_EditablePublicGraphExecutesOnlyNextOpenCrossovers(bool explicitClock, bool connectedValues)
    {
        StrategyGraph graph = SmaStrategyGraphGenerator.Generate(explicitClock: explicitClock, connectedValues: connectedValues);
        BacktestResult result = Run(graph, SmaCandles());
        BacktestLedgerEntry[] fills = Fills(result);

        graph.TemplateKind.ShouldBe(StrategyTemplateKind.SmaCrossover);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        fills.Length.ShouldBe(2);
        fills.Select(entry => entry.Fill!.Side).ShouldBe(new[] { OrderSide.Buy, OrderSide.Sell });
        fills.Select(entry => entry.Context.CandleIndex).ShouldBe(new[] { 4, 6 });
        fills.ShouldAllBe(entry => entry.Context.Phase == PricePhase.Open);
        fills.Select(entry => entry.Fill!.Price).ShouldBe(new[] { 6m, 1m });
        Submitted(result).Select(entry => entry.Context.CandleIndex).ShouldBe(new[] { 3, 5 });
        result.Terminal.Account.QuoteReserved.ShouldBe(0m);
        result.Terminal.Account.BaseReserved.ShouldBe(0m);
        Reconcile(result);
    }

    [Test]
    public void Sma_UserDefaultsCompleteBothSidesAfterFiftyCandleWarmup()
    {
        StrategyDefinition definition = SmaStrategyBlockFactory.Create();
        StrategyGraph graph = new StrategyDefinitionLowerer(ProductionTradingNodeTests.Catalog).Lower(definition).Graph;
        decimal[] prices = Enumerable.Range(start: 0, count: 60)
            .Select(index => 100m - index)
            .Concat(Enumerable.Range(start: 0, count: 30).Select(index => 42m + index))
            .Concat(Enumerable.Range(start: 0, count: 40).Select(index => 70m - index))
            .ToArray();
        BacktestResult result = Run(graph, prices.Select((price, index) => Candle(index, price, price, price, price)).ToArray());

        definition.Parameters.Single(parameter => parameter.Name == "fastPeriod").DefaultValue.ShouldBe(20m);
        definition.Parameters.Single(parameter => parameter.Name == "slowPeriod").DefaultValue.ShouldBe(50m);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        Fills(result).Select(entry => entry.Fill!.Side).ShouldBe(new[] { OrderSide.Buy, OrderSide.Sell });
        Fills(result).ShouldAllBe(entry => entry.Context.CandleIndex >= 50 && entry.Context.Phase == PricePhase.Open);
    }

    [Test]
    public void Ladder_ZeroFeeFixtureImplementsEveryDocumentedStepAndFreshRestart()
    {
        StrategyGraph graph = LadderStrategyGraphGenerator.Generate();
        BacktestResult result = Run(graph, [Candle(index: 0, open: 100m, low: 85m, high: 110m, close: 105m)]);
        BacktestLedgerEntry[] fills = Fills(result);
        BacktestLedgerEntry[] submissions = Submitted(result);

        graph.TemplateKind.ShouldBe(StrategyTemplateKind.Ladder);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        fills.Length.ShouldBe(4);
        fills[0].Fill!.Price.ShouldBe(100m);
        fills[0].Fill!.GrossBase.ShouldBe(1m);
        fills[0].Fill!.QuoteChange.ShouldBe(-100m);
        submissions.ShouldContain(entry => entry.Order!.Tag == "ladder.exit" && entry.Order.LimitPrice == 110m && entry.Order.Quantity == 1m);
        submissions.ShouldContain(entry => entry.Order!.Tag == "ladder.average" && entry.Order.LimitPrice == 90m);
        fills[1].Fill!.Price.ShouldBe(90m);
        fills[1].Fill!.GrossBase.ShouldBe(1.111111m);
        fills[1].Fill!.QuoteChange.ShouldBe(-99.99999m);
        BacktestLedgerEntry replacement = submissions.Single(entry => entry.Order!.Tag == "ladder.exit" && entry.Order.LimitPrice == 104.210m);
        replacement.Order!.Quantity.ShouldBe(2.111111m);
        BacktestLedgerEntry oldExitCancellation = result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Cancelled && entry.Order!.LimitPrice == 110m);
        oldExitCancellation.Sequence.ShouldBeLessThan(replacement.Sequence);
        submissions.ShouldContain(entry => entry.Order!.Tag == "ladder.average" && entry.Order.LimitPrice == 81m);
        fills[2].Fill!.Side.ShouldBe(OrderSide.Sell);
        fills[2].Fill!.Price.ShouldBe(104.210m);
        fills[2].Fill!.GrossBase.ShouldBe(2.111111m);
        fills[2].Fill!.QuoteChange.ShouldBe(219.99887731m);
        fills.ShouldNotContain(entry => entry.Fill!.Price == 81m || entry.Fill.Price == 110m);
        BacktestLedgerEntry averagingCancellation = result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Cancelled && entry.Order!.LimitPrice == 81m);
        BacktestLedgerEntry restart = submissions.Where(entry => entry.Order!.Tag == "ladder.entry").Last();
        averagingCancellation.Sequence.ShouldBeLessThan(restart.Sequence);
        fills[3].Order!.OriginNodeId.ShouldBe("entry");
        fills[3].Fill!.Price.ShouldBe(104.210m);
        fills[3].Context.MarketPrice.ShouldBe(104.210m);
        fills[3].Context.Phase.ShouldBe(PricePhase.LowToHigh);
        result.Terminal.Account.Orders.ShouldBeEmpty();
        result.Terminal.Account.QuoteReserved.ShouldBe(0m);
        result.Terminal.Account.BaseReserved.ShouldBe(0m);
        Reconcile(result);
    }

    [Test]
    public void Sma_FullFeeAccountingFixtureRetainsNetBaseAndDoesNotDoubleSubtractFees()
    {
        Candle[] candles = SmaCandles()
            .Select((candle, index) =>
                Candle(
                    index,
                    open: 100000m,
                    Math.Min(val1: 100000m, candle.Close * 100000m),
                    Math.Max(val1: 100000m, candle.Close * 100000m),
                    candle.Close * 100000m))
            .ToArray();
        var observer = Substitute.For<IBacktestObserver>();
        var snapshots = new List<CandleSnapshot>();
        observer.When(value => value.OnCandleProcessed(Arg.Any<CandleSnapshot>())).Do(call => snapshots.Add(call.Arg<CandleSnapshot>()!));
        BacktestResult result = Run(SmaStrategyGraphGenerator.Generate(), candles, fee: 0.001m, observer);
        Fill buy = Fills(result)[0].Fill!;
        Fill sell = Fills(result)[1].Fill!;

        buy.Price.ShouldBe(100000m);
        buy.BaseChange.ShouldBe(0.000999m);
        snapshots.Single(snapshot => snapshot.Context.CandleIndex == 4).Account.CostBasis.ShouldBe(100m);
        snapshots.Single(snapshot => snapshot.Context.CandleIndex == 4).Account.QuoteTotal.ShouldBe(900m);
        sell.Price.ShouldBe(100000m);
        sell.QuoteChange.ShouldBe(99.8001m);
        result.Terminal.Account.QuoteTotal.ShouldBe(999.8001m);
        result.Terminal.Account.BaseTotal.ShouldBe(0m);
        result.Terminal.Account.RealizedPnl.ShouldBe(-0.1999m);
        result.Terminal.TotalPnl.ShouldBe(-0.1999m);
        Reconcile(result);
    }

    [Test]
    public void Sma_SellSignalQueuesPositiveBaseDespiteLowClosePreviewAndFillsAtValidNextOpen()
    {
        Candle[] candles = SmaCandles();
        candles[4] = Candle(index: 4, open: 100000m, low: 6m, high: 100000m, close: 6m);
        candles[6] = Candle(index: 6, open: 100000m, low: 1m, high: 100000m, close: 1m);
        BacktestResult result = Run(SmaStrategyGraphGenerator.Generate(), candles, fee: 0.001m);
        BacktestLedgerEntry sell = Submitted(result).Single(entry => entry.Order!.Side == OrderSide.Sell);

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        sell.Context.CandleIndex.ShouldBe(5);
        sell.Context.Phase.ShouldBe(PricePhase.Close);
        sell.Order!.Quantity.ShouldBe(0.000999m);
        (sell.Order.Quantity.ShouldNotBeNull() * sell.Context.MarketPrice).ShouldBeLessThan(1m);
        Fills(result).Last().Fill!.Price.ShouldBe(100000m);
        result.Terminal.Account.QuoteTotal.ShouldBe(999.8001m);
        result.Terminal.Account.BaseTotal.ShouldBe(0m);
        Reconcile(result);
    }

    [TestCase(arg1: 5, arg2: 20, arg3: 50)]
    [TestCase(arg1: 15, arg2: 5, arg3: 120)]
    public void Ladder_EditingDropRiseAndBudgetChangesVisiblePricesAndSizes(int dropPercent, int risePercent, int budget)
    {
        decimal drop = dropPercent / 100m;
        decimal rise = risePercent / 100m;
        BacktestResult result = Run(
            LadderStrategyGraphGenerator.Generate(budget, allocationCap: 500m, drop, rise),
            [Candle(index: 0, open: 100m, low: 100m, high: 100m, close: 100m)]);
        BacktestLedgerEntry[] submissions = Submitted(result);

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        Fills(result).Single().Fill!.GrossBase.ShouldBe(budget / 100m);
        submissions.Single(entry => entry.Order!.Tag == "ladder.average").Order!.LimitPrice.ShouldBe(100m * (1m - drop));
        submissions.Single(entry => entry.Order!.Tag == "ladder.exit").Order!.LimitPrice.ShouldBe(100m * (1m + rise));
        Reconcile(result);
    }

    [Test]
    public void BoundInlineAndConnectedValuesAndCompactExplicitClocksProduceEquivalentFacts()
    {
        BacktestResult baseline = Run(SmaStrategyGraphGenerator.Generate(), SmaCandles());
        BacktestResult connected = Run(SmaStrategyGraphGenerator.Generate(explicitClock: true, connectedValues: true), SmaCandles());
        connected.Ledger.ShouldBe(baseline.Ledger);
        connected.Terminal.TotalPnl.ShouldBe(baseline.Terminal.TotalPnl);
        Candle[] ladderCandles = [Candle(index: 0, open: 100m, low: 85m, high: 110m, close: 105m)];
        BacktestResult inlineLadder = Run(LadderStrategyGraphGenerator.Generate(), ladderCandles);
        BacktestResult connectedLadder = Run(LadderStrategyGraphGenerator.Generate(connectedValues: true), ladderCandles);
        connectedLadder.Ledger.ShouldBe(inlineLadder.Ledger);
        connectedLadder.Terminal.TotalPnl.ShouldBe(inlineLadder.Terminal.TotalPnl);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FixedLiteralAndConnectedConstantProduceEquivalentTemplateFacts(bool ladder)
    {
        StrategyGraph graph = ladder ? LadderStrategyGraphGenerator.Generate() : SmaStrategyGraphGenerator.Generate();
        string action = ladder ? "entry" : "buy";
        GraphNode original = graph.Nodes.Single(node => node.Id == action);
        var literalParameters = new Dictionary<string, ParamValue>(original.Parameters) { ["amount"] = Number(100m) };
        StrategyGraph literal = ReplaceNode(graph, action, original.TypeKey, literalParameters);
        var connectedParameters = new Dictionary<string, ParamValue>(original.Parameters);
        connectedParameters.Remove("amount");
        StrategyGraph connected = ReplaceNode(graph, action, original.TypeKey, connectedParameters);
        var constant = new GraphNode(
            "literalBudget",
            "Constant",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["value"] = Number(100m) },
            ValueType.QuoteAmount);
        connected = new StrategyGraph(
            connected.Nodes.Append(constant),
            connected.Connections.Append(Wire("literalBudget", "out", action, "amount")),
            connected.TemplateKind);
        Candle[] candles = ladder ? [Candle(index: 0, open: 100m, low: 85m, high: 110m, close: 105m)] : SmaCandles();
        BacktestResult literalResult = Run(literal, candles);
        BacktestResult connectedResult = Run(connected, candles);

        literalResult.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        connectedResult.Ledger.ShouldBe(literalResult.Ledger);
        connectedResult.Terminal.TotalPnl.ShouldBe(literalResult.Terminal.TotalPnl);
    }

    [Test]
    public void Ladder_FeeAveragingReplacesExitUsingNetQuantityAndFeeInclusiveCost()
    {
        BacktestResult result = Run(
            LadderStrategyGraphGenerator.Generate(),
            [Candle(index: 0, open: 100m, low: 85m, high: 100m, close: 95m)],
            fee: 0.001m);
        BacktestLedgerEntry[] fills = Fills(result);
        Fill first = fills[0].Fill!;
        Fill second = fills[1].Fill!;
        decimal quantity = first.BaseChange + second.BaseChange;
        decimal cost = -first.QuoteChange - second.QuoteChange;
        decimal expectedExit = decimal.Floor(cost / quantity * 1.1m / 0.001m) * 0.001m;
        BacktestLedgerEntry replacement = Submitted(result).Last(entry => entry.Order!.Tag == "ladder.exit");

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        fills.Length.ShouldBe(2);
        first.BaseChange.ShouldBe(0.999m);
        second.BaseChange.ShouldBe(1.109999m);
        replacement.Order!.Quantity.ShouldBe(quantity);
        replacement.Order.LimitPrice.ShouldBe(expectedExit);
        result.Terminal.Account.BaseTotal.ShouldBe(quantity);
        result.Terminal.Account.CostBasis.ShouldBe(cost);
        result.Terminal.Account.FeesBase.ShouldBe(first.Fee + second.Fee);
        Submitted(result).Last(entry => entry.Order!.Tag == "ladder.average").Order!.LimitPrice.ShouldBe(81m);
        Reconcile(result);
    }

    [TestCase("entry")]
    [TestCase("exit")]
    [TestCase("average")]
    public void Ladder_RealCloseRecoveryRoutesToEachSharedActionOnce(string branch)
    {
        StrategyGraph graph = LadderStrategyGraphGenerator.Generate();
        var silent = new GraphNode(
            "silent",
            "OnOrderFilled",
            schemaVersion: 1,
            new Dictionary<string, ParamValue> { ["tag"] = Text("never") });
        graph = new StrategyGraph(graph.Nodes.Append(silent), graph.Connections, graph.TemplateKind);

        if (branch == "entry")
        {
            graph = ReplaceNode(graph, "start", "OnOrderFilled", new Dictionary<string, ParamValue> { ["tag"] = Text("never") });
        }
        else
        {
            string target = branch == "exit" ? "buyFills" : "averageTriggers";
            graph = Rewire(graph, new PortReference(target, "a"), new PortReference("silent", "out"));
        }

        BacktestResult result = Run(
            graph,
            [Candle(index: 0, open: 100m, low: 100m, high: 100m, close: 100m), Candle(index: 1, open: 100m, low: 100m, high: 100m, close: 100m)]);
        BacktestLedgerEntry[] recovered = Submitted(result)
            .Where(entry => entry.Order!.Tag == "ladder." + branch && entry.Context.Phase == PricePhase.Close)
            .ToArray();

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        recovered.Length.ShouldBe(1);
        recovered[0].Context.CandleIndex.ShouldBe(0);
        recovered[0].Order!.EligibleFromCandleIndex.ShouldBe(1);
        Submitted(result).Count(entry => entry.Order!.Tag == "ladder." + branch).ShouldBe(1);
        Reconcile(result);
    }

    [Test]
    public void Ladder_RejectedReplacementProtectivelyCancelsAveragingWithoutImmediateRestart()
    {
        StrategyGraph graph = LadderStrategyGraphGenerator.Generate();
        var doubleAmount = new GraphNode(
            "doubleAmount",
            "Constant",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["value"] = Number(2m) },
            ValueType.Scalar);
        var oversized = new GraphNode(
            "oversized",
            "Arithmetic",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["op"] = Text("Multiply") });
        var seedAverage = new GraphNode(
            "seedAverage",
            "LimitBuy",
            schemaVersion: 1,
            new Dictionary<string, ParamValue> { ["amount"] = Number(50m), ["price"] = Number(50m), ["tag"] = Text("ladder.average") },
            evaluationMode: ActionEvaluationMode.OnEvent);
        graph = new StrategyGraph(
            graph.Nodes.Concat(new[] { doubleAmount, oversized, seedAverage }),
            graph.Connections.Concat(
                new[]
                {
                    Wire("balance", "baseAvailable", "oversized", "a"), Wire("doubleAmount", "out", "oversized", "b"),
                    Wire("entry", "filled", "seedAverage", "trigger")
                }),
            graph.TemplateKind);
        graph = Rewire(graph, new PortReference("exit", "quantity"), new PortReference("oversized", "out"));
        BacktestResult result = Run(graph, [Candle(index: 0, open: 100m, low: 100m, high: 100m, close: 100m)]);
        BacktestLedgerEntry rejection = result.Ledger.First(entry => entry.Kind == BacktestLedgerKind.Rejected && entry.Rejection!.OriginNodeId == "exit");
        BacktestLedgerEntry cancellation = result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Cancelled && entry.Order!.OriginNodeId == "seedAverage");

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        rejection.Rejection!.Reason.ShouldBe(RejectionReason.InsufficientBase);
        cancellation.Sequence.ShouldBeGreaterThan(rejection.Sequence);
        Submitted(result).Count(entry => entry.Order!.Tag == "ladder.entry").ShouldBe(1);
        Fills(result).Length.ShouldBe(1);
        Reconcile(result);
    }

    [TestCase(RejectionReason.ZeroQuantity)]
    [TestCase(RejectionReason.BelowMinQuantity)]
    [TestCase(RejectionReason.BelowMinNotional)]
    public void Ladder_OnlyKnownDustReasonsPermitEntryWithRetainedQuantityAndCost(RejectionReason reason)
    {
        decimal fee = reason == RejectionReason.ZeroQuantity ? 0.5m : 0.1m;
        decimal price = reason == RejectionReason.BelowMinNotional ? 100m : 10000m;
        InstrumentRules rules = reason switch
        {
            RejectionReason.ZeroQuantity => new InstrumentRules(
                quantityStep: 0.01m,
                priceTick: 0.001m,
                minQuantity: 0m,
                minNotional: 1m,
                basePrecision: 3,
                quotePrecision: 8),
            RejectionReason.BelowMinQuantity => new InstrumentRules(
                quantityStep: 0.001m,
                priceTick: 0.001m,
                minQuantity: 0.01m,
                minNotional: 1m,
                basePrecision: 3,
                quotePrecision: 8),
            _ => new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 99m, basePrecision: 6, quotePrecision: 8)
        };
        StrategyGraph graph = Disable(LadderStrategyGraphGenerator.Generate(), "exit");
        var snapshots = new List<CandleSnapshot>();
        var observer = Substitute.For<IBacktestObserver>();
        observer.When(value => value.OnCandleProcessed(Arg.Any<CandleSnapshot>())).Do(call => snapshots.Add(call.Arg<CandleSnapshot>()!));
        BacktestResult result = Run(
            graph,
            [Candle(index: 0, price, price, price, price), Candle(index: 1, price, price, price, price)],
            fee,
            observer,
            rules);

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        BacktestLedgerEntry[] entries = Submitted(result).Where(entry => entry.Order!.Tag == "ladder.entry").ToArray();
        entries.ShouldContain(entry => entry.Context.Phase == PricePhase.Close && entry.Context.CandleIndex == 0);
        snapshots[0].Account.BaseTotal.ShouldBeGreaterThan(0m);
        snapshots[0].Account.CostBasis.ShouldBe(100m);
        result.Terminal.Account.BaseTotal.ShouldBeGreaterThan(snapshots[0].Account.BaseTotal);
        result.Terminal.Account.CostBasis.ShouldBe(200m);
        Fills(result).Length.ShouldBe(2);
        Reconcile(result);
    }

    [Test]
    public void Ladder_NonDustPreviewFailureDoesNotClassifyPositivePositionAsFlatOrRestart()
    {
        StrategyGraph graph = Disable(LadderStrategyGraphGenerator.Generate(), "exit");
        var twice = new GraphNode(
            "twice",
            "Constant",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["value"] = Number(2m) },
            ValueType.Scalar);
        var quantity = new GraphNode("previewQuantity", "Arithmetic", schemaVersion: 2, new Dictionary<string, ParamValue> { ["op"] = Text("Multiply") });
        graph = new StrategyGraph(
            graph.Nodes.Concat(new[] { twice, quantity }),
            graph.Connections.Concat(
                new[]
                {
                    Wire("twice", "out", "previewQuantity", "a"), Wire("position", "quantity", "previewQuantity", "b")
                }),
            graph.TemplateKind);
        graph = Rewire(graph, new PortReference("wholePreview", "quantity"), new PortReference("previewQuantity", "out"));
        BacktestResult result = Run(
            graph,
            [Candle(index: 0, open: 100m, low: 100m, high: 100m, close: 100m), Candle(index: 1, open: 100m, low: 100m, high: 100m, close: 100m)]);

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        Submitted(result).Count(entry => entry.Order!.Tag == "ladder.entry").ShouldBe(1);
        Fills(result).Length.ShouldBe(1);
        result.Terminal.Account.BaseTotal.ShouldBe(1m);
        result.Terminal.Account.CostBasis.ShouldBe(100m);
        Reconcile(result);
    }

    private static StrategyGraph Disable(StrategyGraph graph, string id)
    {
        GraphNode original = graph.Nodes.Single(node => node.Id == id);
        var parameters = new Dictionary<string, ParamValue>(original.Parameters) { ["enabled"] = new FixedParamValue(new BooleanParamLiteral(false)) };
        StrategyGraph changed = ReplaceNode(graph, id, original.TypeKey, parameters);

        return new StrategyGraph(changed.Nodes, changed.Connections.Where(wire => wire.Target != new PortReference(id, "enabled")), changed.TemplateKind);
    }

    private static StrategyGraph ReplaceNode(StrategyGraph graph, string id, string type, IReadOnlyDictionary<string, ParamValue> parameters)
    {
        return new StrategyGraph(
            graph.Nodes.Select(node => node.Id == id
                ? new GraphNode(id, type, schemaVersion: 1, parameters, node.DeclaredValueType, node.EvaluationMode)
                : node),
            graph.Connections,
            graph.TemplateKind);
    }

    private static StrategyGraph Rewire(StrategyGraph graph, PortReference target, PortReference source)
    {
        return new StrategyGraph(graph.Nodes, graph.Connections.Select(wire => wire.Target == target ? new GraphConnection(source, target) : wire), graph.TemplateKind);
    }

    private static GraphConnection Wire(string source, string output, string target, string input)
    {
        return new GraphConnection(new PortReference(source, output), new PortReference(target, input));
    }

    private static FixedParamValue Number(decimal value)
    {
        return new FixedParamValue(new DecimalParamLiteral(value));
    }

    private static FixedParamValue Text(string value)
    {
        return new FixedParamValue(new TextParamLiteral(value));
    }

    private static BacktestResult Run(
        StrategyGraph graph,
        IEnumerable<Candle> replay,
        decimal fee = 0m,
        IBacktestObserver? observer = null,
        InstrumentRules? rules = null)
    {
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(graph, Definitions);
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        var input = new BacktestInput(
            compilation.Graph.ShouldNotBeNull(),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, fee, fee),
            rules ?? new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            Timeframe.Parse("1h"),
            Array.Empty<Candle>(),
            replay);

        return new BacktestEngine().Run(input, observer);
    }

    private static Candle[] SmaCandles()
    {
        decimal[] closes = [4m, 3m, 2m, 5m, 6m, 2m, 1m];

        return closes.Select((price, index) => Candle(index, price, price, price, price)).ToArray();
    }

    private static Candle Candle(int index, decimal open, decimal low, decimal high, decimal close)
    {
        return new Candle(DateTimeOffset.UnixEpoch.AddHours(index), open, high, low, close, Volume: 1m);
    }

    private static BacktestLedgerEntry[] Fills(BacktestResult result)
    {
        return result.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled).ToArray();
    }

    private static BacktestLedgerEntry[] Submitted(BacktestResult result)
    {
        return result.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Submitted).ToArray();
    }

    private static void Reconcile(BacktestResult result)
    {
        result.Terminal.TotalPnl.ShouldBe(result.Terminal.Account.RealizedPnl + result.Terminal.UnrealizedPnl);
    }
}