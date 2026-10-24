using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

/// <summary>
///     The eighteen named execution-contract acceptance fixtures driven through the complete pure replay engine.
/// </summary>
public sealed class ContractFixtureTests
{
    [Test]
    public void ConstantValuesChangeWithoutAnEvent_NoOrder()
    {
        foreach (decimal amount in new[] { 10m, 20m })
        {
            var graph = new StrategyGraph([TradingNode("event", "OnOrderFilled"), Buy("buy", amount)], [Wire("event", "buy", "trigger")]);
            Run(graph, [Bar(index: 0), Bar(index: 1)]).Ledger.ShouldBeEmpty();
        }
    }

    [Test]
    public void InlineLiteralReplacedByEquivalentConnectedConstant_SameNormalizedFacts()
    {
        StrategyGraph inline = StartBuy();
        var connected = new StrategyGraph(
            inline.Nodes.Select(node => node.Id == "buy" ? Buy("buy", amount: null) : node)
                .Append(Node("amount", "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(100m) }, ValueType.QuoteAmount)),
            inline.Connections.Append(Wire("amount", "buy", "amount")));
        Run(inline, [Bar(index: 0)]).Ledger.ShouldBe(Run(connected, [Bar(index: 0)]).Ledger);
    }

    [Test]
    public void CompactCloseModeAndExplicitEventConnection_SameNextOpenEligibility()
    {
        var compact = new StrategyGraph([Buy("buy", mode: ActionEvaluationMode.OnCandleClose)], []);
        var explicitClock = new StrategyGraph([Node("close", "OnCandleClosed"), Buy("buy")], [Wire("close", "buy", "trigger")]);
        BacktestResult a = Run(compact, [Bar(index: 0), Bar(index: 1, open: 120m)]);
        a.Ledger.ShouldBe(Run(explicitClock, [Bar(index: 0), Bar(index: 1, open: 120m)]).Ledger);
        a.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Filled).Fill!.Price.ShouldBe(120m);
    }

    [Test]
    public void GlobalAndDirectFilledRoutesConverge_OneDownstreamAction()
    {
        var graph = new StrategyGraph(
            [Node("start", "OnStart"), Buy("buy"), TradingNode("global", "OnOrderFilled"), Node("merge", "MergeEvents"), Limit("follow", price: 50m)],
            [Wire("start", "buy", "trigger"), Edge("buy", "filled", "merge", "a"), Wire("global", "merge", "b"), Wire("merge", "follow", "trigger")]);
        BacktestResult result = Run(graph, [Bar(index: 0)]);
        BacktestLedgerEntry fill = result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Filled);
        BacktestLedgerEntry follow = result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Submitted && entry.Order!.OriginNodeId == "follow");
        follow.CausationId.ShouldBe(fill.OccurrenceId);
    }

    [Test]
    public void OldTagReusedByDifferentNode_LateFillKeepsOriginalOwner()
    {
        var graph = new StrategyGraph(
            [Node("start", "OnStart"), Buy("old", tag: "shared"), Buy("new", amount: 10m, "shared"), Limit("proof", price: 50m)],
            [Wire("start", "old", "trigger"), Edge("old", "filled", "new", "trigger"), Edge("old", "filled", "proof", "trigger")]);
        BacktestResult result = Run(graph, [Bar(index: 0)]);
        BacktestLedgerEntry[] fills = result.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled).ToArray();
        fills.Select(entry => entry.Order!.OriginNodeId).ShouldBe(new[] { "old", "new" });
        result.Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Submitted && entry.Order!.OriginNodeId == "proof").ShouldBe(1);
        result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Submitted && entry.Order!.OriginNodeId == "proof").CausationId.ShouldBe(fills[0].OccurrenceId);
    }

    [Test]
    public void SmaReadOnFiveOrderEvents_DoesNotAdvanceSamples()
    {
        var nodes = new List<GraphNode>
        {
            Node("close"), Node("sma", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(2m) }), TradingNode("global", "OnOrderFilled"),
            Limit("proof", price: null)
        };
        var wires = new List<GraphConnection> { Wire("close", "sma"), Wire("global", "proof", "trigger"), Wire("sma", "proof", "price") };

        for (var i = 0; i < 5; i++)
        {
            nodes.Add(Buy("chain" + i, amount: 1m, mode: i == 0 ? ActionEvaluationMode.OnCandleClose : ActionEvaluationMode.OnEvent));

            if (i > 0)
            {
                wires.Add(Edge("chain" + (i - 1), "filled", "chain" + i, "trigger"));
            }
        }

        BacktestResult result = Run(new StrategyGraph(nodes, wires), [Bar(index: 0), Bar(index: 1)]);
        result.Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Filled).ShouldBe(5);
        result.Ledger.ShouldNotContain(entry => entry.Order != null && entry.Order.OriginNodeId == "proof");
    }

    [Test]
    public void DuplicateFanOut_ReachesActionOncePerOccurrence()
    {
        var graph = new StrategyGraph(
            [Node("start", "OnStart"), Node("merge", "MergeEvents"), Buy("buy")],
            [Wire("start", "merge", "a"), Wire("start", "merge", "b"), Wire("merge", "buy", "trigger")]);
        Run(graph, [Bar(index: 0)]).Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Submitted).ShouldBe(1);
    }

    [Test]
    public void IndependentBuysRequestAllQuote_StableFirstAndVisibleSecondRejection()
    {
        var graph = new StrategyGraph(
            [Node("start", "OnStart"), TradingNode("balance", "Balance"), Buy("a", amount: null), Buy("b", amount: null)],
            [
                Wire("start", "a", "trigger"), Wire("start", "b", "trigger"), Edge("balance", "quoteAvailable", "a", "amount"),
                Edge("balance", "quoteAvailable", "b", "amount")
            ]);
        BacktestResult result = Run(graph, [Bar(index: 0)]);
        result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Filled).Order!.OriginNodeId.ShouldBe("a");
        result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Rejected).Rejection!.Reason.ShouldBe(RejectionReason.InsufficientQuote);
        result.Terminal.Account.QuoteAvailable.ShouldBe(0m);
    }

    [Test]
    public void CancelAbsentTag_CompletedStillSubmitsReplacement()
    {
        var graph = new StrategyGraph(
            [Node("start", "OnStart"), Cancel("cancel", "absent"), Buy("replacement")],
            [Wire("start", "cancel", "trigger"), Edge("cancel", "completed", "replacement", "trigger")]);
        BacktestResult result = Run(graph, [Bar(index: 0)]);
        result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Filled).Order!.OriginNodeId.ShouldBe("replacement");
        result.Ledger.ShouldNotContain(entry => entry.Kind == BacktestLedgerKind.Cancelled);
    }

    [Test]
    public void CancelOldFullPositionExitAfterAveraging_ReplacementSeesReleasedAndAcquiredBase()
    {
        BacktestResult result = Run(LadderStrategyGraphGenerator.Generate(), [Bar(index: 0, open: 100m, low: 85m, high: 110m, close: 105m)]);
        BacktestLedgerEntry replacement = result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Submitted && entry.Order!.LimitPrice == 104.210m);
        replacement.Order!.Quantity.ShouldBe(2.111111m);
        result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Cancelled && entry.Order!.LimitPrice == 110m).Sequence.ShouldBeLessThan(replacement.Sequence);
    }

    [Test]
    public void MarketBuyAtClose_NextOpenGapUsesCapturedBudgetAndSlippage()
    {
        var graph = new StrategyGraph([Buy("buy", mode: ActionEvaluationMode.OnCandleClose)], []);
        BacktestResult result = Run(graph, [Bar(index: 0), Bar(index: 1, open: 200m)], slippage: 0.1m);
        Fill fill = result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Filled).Fill!;
        fill.Price.ShouldBe(220m);
        fill.GrossBase.ShouldBe(0.454545m);
        (-fill.QuoteChange).ShouldBeLessThanOrEqualTo(100m);
    }

    [Test]
    public void LimitAtCloseAfterLowCrossed_NoRetrospectiveFill()
    {
        var graph = new StrategyGraph([Limit("limit", price: 90m, ActionEvaluationMode.OnCandleClose)], []);
        BacktestResult result = Run(graph, [Bar(index: 0, low: 80m), Bar(index: 1, low: 95m)]);
        result.Ledger.ShouldNotContain(entry => entry.Kind == BacktestLedgerKind.Filled);
        result.Ledger.First(entry => entry.Kind == BacktestLedgerKind.Submitted).Order!.EligibleFromCandleIndex.ShouldBe(1);
    }

    [Test]
    public void IntrabarFillCreatesMarketOrder_UsesReachedPathPrice()
    {
        var graph = new StrategyGraph(
            [Node("start", "OnStart"), Limit("limit", price: 90m), Buy("reaction", amount: 10m)],
            [Wire("start", "limit", "trigger"), Edge("limit", "filled", "reaction", "trigger")]);
        BacktestResult result = Run(graph, [Bar(index: 0, low: 85m)]);
        Fill[] fills = result.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled).Select(entry => entry.Fill!).ToArray();
        fills.Select(fill => fill.Price).ShouldBe(new[] { 90m, 90m });
        result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Filled && entry.Order!.OriginNodeId == "reaction").Context.Phase.ShouldBe(PricePhase.OpenToLow);
    }

    [Test]
    public void ExitFillCancelsAverageAtSamePrice_NoLaterCancelledFill()
    {
        BacktestResult result = Run(LadderStrategyGraphGenerator.Generate(), [Bar(index: 0, low: 85m, high: 110m, close: 105m)]);
        result.Ledger.ShouldContain(entry => entry.Kind == BacktestLedgerKind.Cancelled && entry.Order!.LimitPrice == 81m);
        result.Ledger.ShouldNotContain(entry => entry.Kind == BacktestLedgerKind.Filled && entry.Order!.LimitPrice == 81m);
        BacktestLedgerEntry restart = result.Ledger.Last(entry => entry.Kind == BacktestLedgerKind.Filled);
        restart.Fill!.Price.ShouldBe(104.210m);
    }

    [Test]
    public void FinalCandleNextOpenOrder_ReleasedWithoutFill()
    {
        var graph = new StrategyGraph([Buy("buy", mode: ActionEvaluationMode.OnCandleClose)], []);
        BacktestResult result = Run(graph, [Bar(index: 0)]);
        result.Ledger.Select(entry => entry.Kind).ShouldBe(new[] { BacktestLedgerKind.Submitted, BacktestLedgerKind.ReleasedTerminal });
        result.Terminal.Account.QuoteReserved.ShouldBe(0m);
    }

    [Test]
    public void NonSellableDust_RetainsQuantityCostAndMarkedEquity()
    {
        var graph = new StrategyGraph(
            [
                Node("start", "OnStart"), Buy("buy"), TradingNode("balance", "Balance"),
                TradingNode("sell", "MarketSell", new Dictionary<string, ParamValue> { ["tag"] = Text("sell") }, ActionEvaluationMode.OnCandleClose)
            ],
            [Wire("start", "buy", "trigger"), Edge("balance", "baseAvailable", "sell", "quantity")]);
        var rules = new InstrumentRules(quantityStep: 1m, priceTick: 0.01m, minQuantity: 0m, minNotional: 0m, basePrecision: 2, quotePrecision: 2);
        BacktestResult result = Run(graph, [Bar(index: 0)], marketFee: 0.9m, rules: rules);
        result.Terminal.Account.BaseTotal.ShouldBe(0.1m);
        result.Terminal.Account.CostBasis.ShouldBe(100m);
        result.Terminal.UnrealizedPnl.ShouldBe(-90m);
        result.Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Filled).ShouldBe(1);
    }

    [Test]
    public void EndlessSamePriceBuySellLoop_FailsAtRecordedBoundAndRetainsActualFill()
    {
        var graph = new StrategyGraph(
            [
                Node("start", "OnStart"), Node("merge", "MergeEvents"), Buy("buy", amount: 1m), TradingNode("position", "Position"),
                TradingNode("sell", "MarketSell", new Dictionary<string, ParamValue> { ["tag"] = Text("sell") }, ActionEvaluationMode.OnEvent), Cancel("extra", "absent")
            ],
            [
                Wire("start", "merge", "a"), Edge("sell", "filled", "merge", "b"), Wire("merge", "buy", "trigger"), Edge("buy", "filled", "sell", "trigger"),
                Edge("position", "quantity", "sell", "quantity"), Wire("start", "extra", "trigger")
            ]);
        BacktestResult result = Run(graph, [Bar(index: 0)]);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Failed);
        result.Terminal.Diagnostic!.Code.ShouldBe("CascadeLimit");
        result.Ledger.ShouldContain(entry => entry.Kind == BacktestLedgerKind.Filled && entry.OccurrenceId == null);
        result.Terminal.Account.QuoteReserved.ShouldBe(0m);
        result.Terminal.Account.BaseReserved.ShouldBe(0m);
        result.Terminal.Equity.ShouldBe(1000m);
        result.Terminal.TotalPnl.ShouldBe(result.Terminal.Account.RealizedPnl + result.Terminal.UnrealizedPnl);
    }

    [Test]
    public void IdenticalFrozenInputs_RepeatSameInstanceAndParallelInstances()
    {
        BacktestInput input = Input(StartBuy(), [Bar(index: 0), Bar(index: 1)]);
        var engine = new BacktestEngine();
        BacktestResult first = engine.Run(input);
        first.Ledger.ShouldBe(engine.Run(input).Ledger);
        var parallel = new BacktestResult[4];
        Parallel.For(fromInclusive: 0, parallel.Length, index => parallel[index] = engine.Run(input));

        foreach (BacktestResult result in parallel)
        {
            result.Ledger.ShouldBe(first.Ledger);
            result.Terminal.Account.BaseTotal.ShouldBe(first.Terminal.Account.BaseTotal);
            result.Terminal.Equity.ShouldBe(first.Terminal.Equity);
        }
    }

    internal static BacktestResult Run(
        StrategyGraph graph,
        IEnumerable<Candle> candles,
        decimal slippage = 0m,
        decimal marketFee = 0m,
        InstrumentRules? rules = null,
        IBacktestObserver? observer = null,
        CancellationToken token = default)
    {
        return new BacktestEngine().Run(Input(graph, candles, slippage, marketFee, rules), observer, token);
    }

    internal static BacktestInput Input(StrategyGraph graph, IEnumerable<Candle> candles, decimal slippage = 0m, decimal marketFee = 0m, InstrumentRules? rules = null)
    {
        TradingGraphCompilationResult compiled = new TradingGraphCompiler().Compile(graph, Catalog);
        compiled.Validation.Diagnostics.ShouldBeEmpty();

        return new BacktestInput(
            compiled.Graph.ShouldNotBeNull(),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage, limitFeeRate: 0m, marketFee),
            rules ?? new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 6),
            Timeframe.Parse("1h"),
            Array.Empty<Candle>(),
            candles);
    }

    internal static Candle Bar(int index, decimal open = 100m, decimal? low = null, decimal? high = null, decimal? close = null)
    {
        decimal end = close ?? open;

        return new Candle(DateTimeOffset.UnixEpoch.AddHours(index), open, high ?? decimal.Max(open, end), low ?? decimal.Min(open, end), end, Volume: 1m);
    }

    internal static StrategyGraph StartBuy()
    {
        return new StrategyGraph([Node("start", "OnStart"), Buy("buy")], [Wire("start", "buy", "trigger")]);
    }

    internal static GraphNode Buy(string id, decimal? amount = 100m, string? tag = null, ActionEvaluationMode mode = ActionEvaluationMode.OnEvent)
    {
        var parameters = new Dictionary<string, ParamValue> { ["tag"] = Text(tag ?? id) };

        if (amount.HasValue)
        {
            parameters["amount"] = Number(amount.Value);
        }

        return TradingNode(id, "MarketBuy", parameters, mode);
    }

    internal static GraphNode Limit(string id, decimal? price, ActionEvaluationMode mode = ActionEvaluationMode.OnEvent)
    {
        var parameters = new Dictionary<string, ParamValue> { ["tag"] = Text(id), ["amount"] = Number(100m) };

        if (price.HasValue)
        {
            parameters["price"] = Number(price.Value);
        }

        return TradingNode(id, "LimitBuy", parameters, mode);
    }

    internal static GraphNode Cancel(string id, string tag)
    {
        return TradingNode(id, "CancelOrder", new Dictionary<string, ParamValue> { ["tag"] = Text(tag) }, ActionEvaluationMode.OnEvent);
    }

    internal static GraphConnection Edge(string source, string port, string target, string input)
    {
        return new GraphConnection(new PortReference(source, port), new PortReference(target, input));
    }
}