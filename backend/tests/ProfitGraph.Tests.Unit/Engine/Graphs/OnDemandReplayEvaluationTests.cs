using System.Text.Json;

using NSubstitute;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class OnDemandReplayEvaluationTests
{
    [Test]
    public void UnusedDispatchArithmeticFailsOnlyOnRetainingPath()
    {
        CompiledTradingGraph graph = Plan(BrokenGraph(used: false));
        StartDispatcher(graph, replay: true).AppliedActions.ShouldBeEmpty();
        var error = Should.Throw<GraphExecutionException>(() => StartDispatcher(graph, replay: false));
        AssertDivisionError(error);
    }

    [TestCase(arg: false)]
    [TestCase(arg: true)]
    public void UsedDispatchArithmeticRetainsExactFailureContext(bool replay)
    {
        CompiledTradingGraph graph = Plan(BrokenGraph(used: true));
        var error = Should.Throw<GraphExecutionException>(() => StartDispatcher(graph, replay));
        AssertDivisionError(error);
    }

    [Test]
    public void UnreachedFilterDoesNotEvaluateBrokenCondition()
    {
        StrategyGraph broken = BrokenGraph(used: false);
        var graph = new StrategyGraph(
            broken.Nodes.Concat(
            [
                Node("close", "OnCandleClosed"), Node("filter", "FilterEvent"), Node("compare", "Compare"),
                TradingNode("buy", "MarketBuy", new Dictionary<string, ParamValue> { ["amount"] = Number(10m), ["tag"] = Text("entry") }, ActionEvaluationMode.OnEvent)
            ]),
            broken.Connections.Concat(
            [
                Wire("close", "filter", "event"), Wire("compare", "filter", "condition"), Wire("divide", "compare", "a"),
                new GraphConnection(new PortReference("balance", "quoteTotal"), new PortReference("compare", "b")), Wire("filter", "buy", "trigger")
            ]));
        StartDispatcher(Plan(graph), replay: true).AppliedActions.ShouldBeEmpty();
    }

    [TestCase("And", arg2: false)]
    [TestCase("Or", arg2: true)]
    public void BooleanOperatorsStillReadBrokenSecondInput(string operation, bool first)
    {
        StrategyGraph broken = BrokenGraph(used: false);
        var graph = new StrategyGraph(
            broken.Nodes.Concat(
            [
                Node("start", "OnStart"), Node("filter", "FilterEvent"), Node("compare", "Compare"), Node("join", operation),
                Node("first", "Constant", new Dictionary<string, ParamValue> { ["value"] = new FixedParamValue(new BooleanParamLiteral(first)) }, ValueType.Boolean),
                TradingNode("buy", "MarketBuy", new Dictionary<string, ParamValue> { ["amount"] = Number(10m), ["tag"] = Text("entry") }, ActionEvaluationMode.OnEvent)
            ]),
            broken.Connections.Concat(
            [
                Wire("start", "filter", "event"), Wire("join", "filter", "condition"), Wire("first", "join", "a"), Wire("compare", "join", "b"),
                Wire("divide", "compare", "a"), new GraphConnection(new PortReference("balance", "quoteTotal"), new PortReference("compare", "b")),
                Wire("filter", "buy", "trigger")
            ]));
        AssertDivisionError(Should.Throw<GraphExecutionException>(() => StartDispatcher(Plan(graph), replay: true)));
    }

    [Test]
    public void EligibleActionRetainsUnavailableEnabledSkip()
    {
        var graph = new StrategyGraph(
            [
                Node("start", "OnStart"), TradingNode("position", "Position"), Node("condition", "Compare"),
                Node("price", "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(100m) }, ValueType.Price),
                TradingNode("buy", "MarketBuy", new Dictionary<string, ParamValue> { ["amount"] = Number(10m), ["tag"] = Text("entry") }, ActionEvaluationMode.OnEvent)
            ],
            [
                Wire("start", "buy", "trigger"), new GraphConnection(new PortReference("position", "averageCost"), new PortReference("condition", "a")),
                Wire("price", "condition", "b"), Wire("condition", "buy", "enabled")
            ]);
        CompiledTradingGraph compiled = Plan(graph);
        DispatchBatch retained = StartDispatcher(compiled, replay: false);
        DispatchBatch replay = StartDispatcher(compiled, replay: true);
        replay.AppliedActions.ShouldBeEmpty();
        replay.Skips.ShouldBe(retained.Skips);
        replay.Skips.Single().Reason.ShouldBe("EnabledUnavailable");
    }

    [Test]
    public void SharedDispatchSourceIsComputedOnceForSiblingActions()
    {
        INodeDefinition source = Source("Counted", ValueType.QuoteAmount, NodeClock.Dispatch);
        source.EvaluatesDispatch.Returns(true);
        source.EvaluateEngine(Arg.Any<NodeEngineEvaluationContext>()).Returns(EngineValue.Numeric(ValueType.QuoteAmount, value: 10m));
        var graph = new StrategyGraph(
            [
                Node("start", "OnStart"), new GraphNode("value", "Counted", schemaVersion: 1, new Dictionary<string, ParamValue>()),
                TradingNode("a", "MarketBuy", new Dictionary<string, ParamValue> { ["tag"] = Text("a") }, ActionEvaluationMode.OnEvent),
                TradingNode("b", "MarketBuy", new Dictionary<string, ParamValue> { ["tag"] = Text("b") }, ActionEvaluationMode.OnEvent)
            ],
            [Wire("start", "a", "trigger"), Wire("start", "b", "trigger"), Wire("value", "a", "amount"), Wire("value", "b", "amount")]);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(graph, Catalog.Append(source).ToArray());
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        StartDispatcher(compilation.Graph.ShouldNotBeNull(), replay: true).AppliedActions.Count.ShouldBe(2);
        source.Received(1).EvaluateEngine(Arg.Any<NodeEngineEvaluationContext>());
    }

    [Test]
    public void ReplayCrossoverPulseIsMaskedOutsideMatchingCloseWithoutAdvancingSamples()
    {
        CompiledTradingGraph graph = Plan(
            new StrategyGraph(
                [Node("price"), Node("previous", "PreviousCandleValue"), Node("cross", "CrossesAbove")],
                [Wire("price", "previous"), Wire("price", "cross", "a"), Wire("previous", "cross", "b")]));
        var evaluator = new CandleValueEvaluator(graph);
        evaluator.CommitClosedCandle(Candle(index: 0, close: 3m), candleIndex: 0);
        evaluator.CommitClosedCandle(Candle(index: 1, close: 2m), candleIndex: 1);
        Candle candle = Candle(index: 2, close: 4m);
        evaluator.CommitClosedCandle(candle, candleIndex: 2);
        BacktestInput input = Input(graph, [candle]);
        var account = new TradingAccount(input.Settings, input.Rules);
        var market = new ReachedMarketContext(candle, candleIndex: 0, candle.OpenTime.AddHours(1), PricePhase.Close, marketPrice: 4m, eligibleFromCandleIndex: 1);
        var context = new DispatchEvaluationContext(AccountSnapshot.Capture(account), input.Rules, input.Settings, market);
        var routed = new EngineValue[graph.SlotCount];

        foreach (bool close in new[] { true, false, true })
        {
            EngineValue[] outputs = evaluator.ReadDispatchForReplay(Event(candle, close), routed, context);
            evaluator.ReadReplayValue(graph.SlotOf(new PortReference("cross", "out")), outputs).Boolean.ShouldBe(close);
            evaluator.ReadReplayValue(graph.SlotOf(new PortReference("previous", "out")), outputs).Number.ShouldBe(2m);
        }
    }

    [Test]
    public void IsAvailableReadsAbsentTradingValueAsFalse()
    {
        CompiledTradingGraph graph = Plan(
            new StrategyGraph(
                [TradingNode("position", "Position"), Node("ready", "IsAvailable")],
                [new GraphConnection(new PortReference("position", "averageCost"), new PortReference("ready", "in"))]));
        var evaluator = new CandleValueEvaluator(graph);
        BacktestInput input = Input(graph, [Candle(index: 0, close: 100m)]);
        var account = new TradingAccount(input.Settings, input.Rules);
        var market = new ReachedMarketContext(
            input.Replay[0],
            candleIndex: 0,
            input.Replay[0].OpenTime.AddHours(1),
            PricePhase.Open,
            marketPrice: 100m,
            eligibleFromCandleIndex: 0);
        var context = new DispatchEvaluationContext(AccountSnapshot.Capture(account), input.Rules, input.Settings, market);
        EngineValue[] outputs = evaluator.ReadDispatchForReplay(Event(input.Replay[0], close: false), new EngineValue[graph.SlotCount], context);
        EngineValue ready = evaluator.ReadReplayValue(graph.SlotOf(new PortReference("ready", "out")), outputs);
        ready.IsAvailable.ShouldBeTrue();
        ready.Boolean.ShouldBeFalse();
    }

    [TestCase("Sma", "fixed")]
    [TestCase("Sma", "walk")]
    [TestCase("Ladder", "fixed")]
    [TestCase("Ladder", "walk")]
    [TestCase("BlockSma", "fixed")]
    [TestCase("BlockSma", "walk")]
    [TestCase("BlockLadder", "fixed")]
    [TestCase("BlockLadder", "walk")]
    public void ReplayRequestsSkipsLedgerAndMetricsMatchRetainingPath(string workload, string pattern)
    {
        StrategyGraph graph = workload switch
        {
            "Sma" => SmaStrategyGraphGenerator.Generate(),
            "Ladder" => LadderStrategyGraphGenerator.Generate(),
            "BlockSma" => new StrategyDefinitionLowerer(Catalog).Lower(SmaStrategyBlockFactory.Create(fastPeriod: 2, slowPeriod: 3)).Graph,
            "BlockLadder" => new StrategyDefinitionLowerer(Catalog).Lower(LadderStrategyBlockFactory.Create()).Graph,
            _ => throw new ArgumentOutOfRangeException(nameof(workload))
        };
        CompiledTradingGraph compiled = Plan(graph);
        Candle[] candles = Candles(pattern);
        BacktestInput input = Input(compiled, candles);
        var evaluator = new CandleValueEvaluator(compiled);
        var router = new EventRouter();
        var routed = new EngineValue[compiled.SlotCount];
        var touched = new List<int>();
        var collector = new ActionCollector();
        var requests = new List<ActionRequest>();
        var skips = new List<ActionSkipDiagnostic>();
        var closed = 0;
        var observed = 0;

        foreach (CompiledTradingNode node in compiled.Nodes)
        {
            foreach ((int slot, ValueType type) in node.OutputSlots)
            {
                routed[slot] = EngineValue.Unavailable(type);
            }
        }

        void CheckBatch(DispatchBatch batch)
        {
            var batchSkips = new List<ActionSkipDiagnostic>();

            foreach (DispatchObservation observation in batch.Observations)
            {
                EventOccurrence occurrence = observation.Occurrence;
                Candle candle = candles[occurrence.CandleIndex];

                if (occurrence.Kind == EventKind.CandleClosed)
                {
                    evaluator.CommitClosedCandle(candle, closed++);
                }

                var market = new ReachedMarketContext(
                    candle,
                    occurrence.CandleIndex,
                    occurrence.CandleCloseTime,
                    occurrence.Phase,
                    occurrence.MarketPrice,
                    occurrence.Phase == PricePhase.Close ? occurrence.CandleIndex + 1 : occurrence.CandleIndex);
                var context = new DispatchEvaluationContext(observation.Account, input.Rules, input.Settings, market);
                router.RouteInto(compiled, occurrence, routed, touched);
                EngineValue[] outputs = evaluator.ReadDispatchForReplay(occurrence, routed, context);
                collector.CollectForReplay(compiled, occurrence, market, outputs, evaluator, requests, skips);
                requests.ShouldBe(observation.Requests);
                batchSkips.AddRange(skips);
                observed++;
            }

            batchSkips.ShouldBe(batch.Skips);
        }

        var retainedMetrics = new MetricsObserver(input.Settings.StartingCapital, input.SampleRanges);
        var replayMetrics = new MetricsObserver(input.Settings.StartingCapital, input.SampleRanges);
        var engine = new BacktestEngine();
        BacktestResult retained = engine.Run(input, retainedMetrics, options: new BacktestRunOptions { RetainDispatchProjections = true, DispatchObserver = CheckBatch });
        BacktestResult replay = engine.Run(input, replayMetrics);
        retained.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        replay.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        observed.ShouldBeGreaterThan(candles.Length);

        if (workload.EndsWith("Ladder", StringComparison.Ordinal) || pattern == "walk")
        {
            retained.FillCount.ShouldBeGreaterThan(0);
        }

        JsonSerializer.SerializeToUtf8Bytes(new { Result = replay, Metrics = replayMetrics.Snapshot })
            .ShouldBe(JsonSerializer.SerializeToUtf8Bytes(new { Result = retained, Metrics = retainedMetrics.Snapshot }));
    }

    private static StrategyGraph BrokenGraph(bool used)
    {
        var nodes = new List<GraphNode>
        {
            TradingNode("balance", "Balance"),
            Node("zero", "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(0m) }, ValueType.Scalar),
            Node("divide", "Arithmetic", new Dictionary<string, ParamValue> { ["op"] = Text("Divide") })
        };
        var wires = new List<GraphConnection>
        {
            new GraphConnection(new PortReference("balance", "quoteTotal"), new PortReference("divide", "a")), Wire("zero", "divide", "b")
        };

        if (used)
        {
            nodes.Add(Node("start", "OnStart"));
            nodes.Add(TradingNode("buy", "MarketBuy", new Dictionary<string, ParamValue> { ["tag"] = Text("entry") }, ActionEvaluationMode.OnEvent));
            wires.Add(Wire("start", "buy", "trigger"));
            wires.Add(Wire("divide", "buy", "amount"));
        }

        return new StrategyGraph(nodes, wires);
    }

    private static void AssertDivisionError(GraphExecutionException error)
    {
        error.NodeId.ShouldBe("divide");
        error.BarIndex.ShouldBe(7);
        error.OccurrenceId.ShouldBe(1L);
        error.InnerException.ShouldBeOfType<DivideByZeroException>();
    }

    private static DispatchBatch StartDispatcher(CompiledTradingGraph graph, bool replay)
    {
        BacktestInput input = Input(graph, [Candle(index: 7, close: 100m)]);
        var dispatcher = new CausalDispatcher(graph, new TradingAccount(input.Settings, input.Rules));
        var context = new ReachedMarketContext(
            input.Replay[0],
            candleIndex: 7,
            input.Replay[0].OpenTime.AddHours(1),
            PricePhase.Open,
            marketPrice: 100m,
            eligibleFromCandleIndex: 7);
        dispatcher.BeginOpen(context);
        dispatcher.EnqueueExternal(EventKind.Start, new StartPayload(), context);

        return replay ? dispatcher.DrainForReplay(CancellationToken.None) : dispatcher.Drain();
    }

    private static CompiledTradingGraph Plan(StrategyGraph graph)
    {
        TradingGraphCompilationResult result = new TradingGraphCompiler().Compile(graph, Catalog);
        result.Validation.Diagnostics.ShouldBeEmpty();

        return result.Graph.ShouldNotBeNull();
    }

    private static BacktestInput Input(CompiledTradingGraph graph, Candle[] candles)
    {
        return new BacktestInput(
            graph,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0.001m, marketFeeRate: 0.001m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            Timeframe.Parse("1h"),
            Array.Empty<Candle>(),
            candles,
            candles.Length > 1 ? OutOfSampleSplit.Create(fraction: 0.25m) : null);
    }

    private static Candle[] Candles(string pattern)
    {
        var random = new Random(20261013);
        var result = new Candle[128];
        var price = 100m;

        for (var index = 0; index < result.Length; index++)
        {
            decimal close = decimal.Clamp(price + random.Next(minValue: -20, maxValue: 21), min: 30m, max: 150m);
            result[index] = pattern == "fixed"
                ? new Candle(DateTimeOffset.UnixEpoch.AddHours(index), Open: 100m, High: 110m, Low: 85m, Close: 105m, Volume: 1m)
                : new Candle(DateTimeOffset.UnixEpoch.AddHours(index), price, decimal.Max(price, close) + 10m, decimal.Min(price, close) - 10m, close, Volume: 1m);
            price = close;
        }

        return result;
    }
}