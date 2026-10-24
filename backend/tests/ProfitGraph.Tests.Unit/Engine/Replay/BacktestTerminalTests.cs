using System.Text.Json;

using NSubstitute;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;
using static ProfitGraph.Tests.Unit.Engine.Replay.ContractFixtureTests;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class BacktestTerminalTests
{
    [TestCase(arg1: false, arg2: false)]
    [TestCase(arg1: true, arg2: false)]
    [TestCase(arg1: false, arg2: true)]
    [TestCase(arg1: true, arg2: true)]
    public void UnrepresentableValuationFailsInsideReplayAndPreservesCommittedFacts(bool observe, bool earlierCandle)
    {
        int index = earlierCandle ? 1 : 0;
        Candle overflowing = Bar(index, open: 1m, low: 1m, decimal.MaxValue, decimal.MaxValue);
        Candle[] candles = earlierCandle ? [Bar(index: 0, open: 1m), overflowing] : [overflowing];
        var observer = Substitute.For<IBacktestObserver>();
        var received = new List<BacktestLedgerEntry>();
        observer.When(value => value.OnLedger(Arg.Any<BacktestLedgerEntry>())).Do(call => received.Add(call.Arg<BacktestLedgerEntry>()!));
        BacktestResult result = Run(StartBuy(), candles, observer: observe ? observer : null);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Failed);
        result.Terminal.Diagnostic.ShouldNotBeNull().Code.ShouldBe("ValuationOverflow");
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "ValuationOverflow" && diagnostic.CandleIndex == index);
        result.Terminal.Context.CandleIndex.ShouldBe(index);
        result.Terminal.Context.MarketPrice.ShouldBe(decimal.MaxValue);
        result.Terminal.ProcessedCandles.ShouldBe(index);
        result.Terminal.Equity.ShouldBeNull();
        result.Terminal.UnrealizedPnl.ShouldBeNull();
        result.Terminal.TotalPnl.ShouldBeNull();
        result.Terminal.Account.QuoteTotal.ShouldBe(900m);
        result.Terminal.Account.BaseTotal.ShouldBe(100m);
        result.Terminal.Account.CostBasis.ShouldBe(100m);
        result.Terminal.Account.QuoteReserved.ShouldBe(0m);
        result.Terminal.Account.BaseReserved.ShouldBe(0m);
        result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Filled).OccurrenceId.ShouldNotBeNull();
        Should.NotThrow(() => JsonSerializer.Serialize(result));

        if (observe)
        {
            received.ShouldBe(result.Ledger);
            observer.Received(index).OnCandleProcessed(Arg.Any<CandleSnapshot>());
            observer.Received(1).OnTerminal(Arg.Is<BacktestTerminalSnapshot>(terminal => terminal != null && terminal.Outcome == BacktestOutcome.Failed));
        }
    }

    [TestCase(arg: false)]
    [TestCase(arg: true)]
    public void MidPathCancellationOrObserverFailureRetainsFillAndReleasesAtReachedPrice(bool cancel)
    {
        var graph = new StrategyGraph(
            [Node("start", "OnStart"), Limit("near", price: 90m), Limit("far", price: 50m)],
            [Wire("start", "near", "trigger"), Wire("start", "far", "trigger")]);
        using var source = new CancellationTokenSource();
        var observer = Substitute.For<IBacktestObserver>();
        observer.When(value => value.OnLedger(Arg.Is<BacktestLedgerEntry>(entry => entry != null && entry.Kind == BacktestLedgerKind.Filled)))
            .Do(_ =>
            {
                if (cancel)
                {
                    source.Cancel();
                }
                else
                {
                    throw new OperationCanceledException("Unrelated observer failure.");
                }
            });
        BacktestResult result = Run(graph, [Bar(index: 0, low: 85m, high: 110m, close: 105m)], observer: observer, token: source.Token);
        result.Terminal.Outcome.ShouldBe(cancel ? BacktestOutcome.Cancelled : BacktestOutcome.Failed);
        result.Terminal.Context.MarketPrice.ShouldBe(90m);
        result.Terminal.Context.Phase.ShouldBe(PricePhase.OpenToLow);
        result.Terminal.ProcessedCandles.ShouldBe(0);
        result.Terminal.Account.QuoteReserved.ShouldBe(0m);
        result.Terminal.Account.BaseReserved.ShouldBe(0m);
        result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Filled).OccurrenceId.ShouldNotBeNull();
        result.Ledger.ShouldContain(entry => entry.Kind == BacktestLedgerKind.ReleasedTerminal && entry.Order!.OriginNodeId == "far");
        result.Terminal.TotalPnl.ShouldBe(result.Terminal.Account.RealizedPnl + result.Terminal.UnrealizedPnl);
        observer.DidNotReceive().OnCandleProcessed(Arg.Any<CandleSnapshot>());
        observer.Received(1)
            .OnTerminal(Arg.Is<BacktestTerminalSnapshot>(terminal => terminal != null && terminal.Context.MarketPrice == 90m && terminal.Account.QuoteReserved == 0m));
    }

    [Test]
    public void ThrowingDiagnosticObserverStillReceivesCleanedTerminal()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var observer = Substitute.For<IBacktestObserver>();
        observer.When(value => value.OnDiagnostic(Arg.Any<BacktestDiagnostic>())).Do(_ => throw new InvalidOperationException("Diagnostic sink failed."));
        BacktestResult result = Run(StartBuy(), [Bar(index: 0)], observer: observer, token: source.Token);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Failed);
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "ObserverFailure");
        observer.Received(1).OnTerminal(Arg.Any<BacktestTerminalSnapshot>());
    }

    [Test]
    public void FinalProcessedSnapshotAlreadyContainsTerminalRelease()
    {
        var observer = Substitute.For<IBacktestObserver>();
        var graph = new StrategyGraph([Buy("last", mode: ActionEvaluationMode.OnCandleClose)], []);
        BacktestResult result = Run(graph, [Bar(index: 0)], observer: observer);
        observer.Received(1).OnCandleProcessed(Arg.Is<CandleSnapshot>(snapshot => snapshot != null && snapshot.Sequence == 1 && snapshot.Account.QuoteReserved == 0m));
        observer.Received(1).OnTerminal(Arg.Is<BacktestTerminalSnapshot>(terminal => terminal != null && terminal.Outcome == BacktestOutcome.Completed));
        result.Terminal.Account.Orders.ShouldBeEmpty();
    }

    [Test]
    public void WarmupOnlyAdvancesIndicatorsAndFirstReplayStartsItsOwnIndex()
    {
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(StartBuy(), Catalog);
        var input = new BacktestInput(
            compilation.Graph.ShouldNotBeNull(),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 6),
            Timeframe.Parse("1h"),
            [Bar(index: 0), Bar(index: 1)],
            [Bar(index: 2)]);
        var observer = Substitute.For<IBacktestObserver>();
        BacktestResult result = new BacktestEngine().Run(input, observer);
        result.Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Filled).ShouldBe(1);
        result.Ledger.ShouldAllBe(entry => entry.Context.CandleIndex == 0 && entry.Context.Candle.OpenTime == DateTimeOffset.UnixEpoch.AddHours(2));
        observer.Received(1).OnCandleProcessed(Arg.Any<CandleSnapshot>());
    }

    [Test]
    public void NeverReadyHistoryCompletesWithExplanation()
    {
        var graph = new StrategyGraph(
            [Node("close"), Node("sma", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(100m) })],
            [Wire("close", "sma")]);
        BacktestResult result = Run(graph, [Bar(index: 0)]);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "IndicatorsNeverReady");
    }

    [TestCase("PreviousCandleValue")]
    [TestCase("CrossesAbove")]
    [TestCase("CrossesBelow")]
    public void SingleSampleDoesNotMakePreviousOrCrossoverReady(string type)
    {
        bool crossover = type != "PreviousCandleValue";
        var graph = new StrategyGraph(
            [Node("close"), Node("indicator", type)],
            crossover ? [Wire("close", "indicator", "a"), Wire("close", "indicator", "b")] : [Wire("close", "indicator")]);
        BacktestResult first = Run(graph, [Bar(index: 0)]);
        first.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "IndicatorsNeverReady");
        BacktestResult ready = Run(graph, [Bar(index: 0), Bar(index: 1)]);
        ready.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void RoundedFeeCanConsumeGrossWithoutDivisionByZeroOrLostCost()
    {
        var graph = new StrategyGraph([Node("start", "OnStart"), Buy("buy", amount: 0.000001m)], [Wire("start", "buy", "trigger")]);
        var rules = new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.000001m, minQuantity: 0m, minNotional: 0m, basePrecision: 6, quotePrecision: 6);
        BacktestResult result = Run(graph, [Bar(index: 0, open: 1m)], marketFee: 0.000001m, rules: rules);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Filled).Fill!.BaseChange.ShouldBe(0m);
        result.Terminal.Account.BaseTotal.ShouldBe(0m);
        result.Terminal.Account.CostBasis.ShouldBe(0.000001m);
        result.Terminal.TotalPnl.ShouldBe(-0.000001m);
        result.Terminal.TotalPnl.ShouldBe(result.Terminal.Account.RealizedPnl + result.Terminal.UnrealizedPnl);
    }

    [Test]
    public void MoreThanTenThousandFlatCandlesDoNotShareCascadeBudget()
    {
        var graph = new StrategyGraph([Node("close", "OnCandleClosed"), Cancel("cancel", "absent")], [Wire("close", "cancel", "trigger")]);
        BacktestResult result = Run(graph, Enumerable.Range(start: 0, count: 10001).Select(index => Bar(index)));
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Terminal.ProcessedCandles.ShouldBe(10001);
        result.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void InputCopiesCallerHistoryAndRejectsMaximumTimestampOverflowBeforeMutation()
    {
        var candles = new List<Candle> { Bar(index: 0) };
        BacktestInput input = Input(StartBuy(), candles);
        candles.Clear();
        new BacktestEngine().Run(input).Terminal.ProcessedCandles.ShouldBe(1);
        var invalid = new Candle(DateTimeOffset.MaxValue, Open: 1m, High: 1m, Low: 1m, Close: 1m, Volume: 1m);
        Should.Throw<ArgumentException>(() => Input(StartBuy(), [invalid]));
    }

    [Test]
    public void SelectionErrorsPreserveNodeReferencesBeforeReplay()
    {
        var graph = new StrategyGraph(
            [Node("value", "Constant", new Dictionary<string, ParamValue> { ["value"] = new DecimalOptimizeParamValue(Min: 1m, Max: 2m, Step: 1m) })],
            []);
        var error = Should.Throw<BacktestInputException>(() => Input(graph, [Bar(index: 0)]));
        error.Validation.Diagnostics.Single().NodeId.ShouldBe("value");
    }
}