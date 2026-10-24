using System.Text.Json;

using NSubstitute;

using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;

using static ProfitGraph.Tests.Unit.Engine.Replay.ContractFixtureTests;
using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class BacktestRetentionTests
{
    [TestCase(BacktestOutcome.Completed)]
    [TestCase(BacktestOutcome.Cancelled)]
    [TestCase(BacktestOutcome.Failed)]
    public void DroppingLedgerPreservesTerminalCountsAndObserverFacts(BacktestOutcome outcome)
    {
        BacktestInput input = Input(
            new StrategyGraph(
                [Node("start", "OnStart"), Limit("near", price: 90m), Limit("far", price: 50m)],
                [Wire("start", "near", "trigger"), Wire("start", "far", "trigger")]),
            [Bar(index: 0, low: 85m, high: 110m, close: 105m), Bar(index: 1)]);
        BacktestResult retained = Execute(input, outcome, retainLedger: true, out _, out _);
        BacktestResult discarded = Execute(input, outcome, retainLedger: false, out BacktestLedgerEntry[] received, out CandleSnapshot[] candles);
        retained.Terminal.Outcome.ShouldBe(outcome);
        discarded.Ledger.ShouldBeEmpty();
        discarded.LedgerCount.ShouldBe(retained.Ledger.Count);
        discarded.FillCount.ShouldBe(retained.Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Filled));
        discarded.OrderCount.ShouldBe(retained.Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Submitted));
        discarded.LedgerCount.ShouldBe(retained.LedgerCount);
        discarded.FillCount.ShouldBe(retained.FillCount);
        discarded.OrderCount.ShouldBe(retained.OrderCount);
        received.ShouldBe(retained.Ledger);
        candles.Length.ShouldBe(retained.Terminal.ProcessedCandles);
        JsonSerializer.Serialize(discarded.Terminal).ShouldBe(JsonSerializer.Serialize(retained.Terminal));
        discarded.Diagnostics.ShouldBe(retained.Diagnostics);
    }

    [Test]
    public void NarrowObserverReceivesScalarsAndEveryFactWithoutFullCandleCallback()
    {
        BacktestInput input = Input(StartBuy(), [Bar(index: 0), Bar(index: 1, close: 110m)]);
        var full = Substitute.For<IBacktestObserver>();
        var snapshots = new List<CandleSnapshot>();
        full.When(observer => observer.OnCandleProcessed(Arg.Any<CandleSnapshot>())).Do(call => snapshots.Add(call.Arg<CandleSnapshot>()!));
        var narrow = Substitute.For<IBacktestMetricsObserver>();
        var observations = new List<CandleMetricObservation>();
        narrow.When(observer => observer.OnCandleMetrics(Arg.Any<CandleMetricObservation>())).Do(call => observations.Add(call.Arg<CandleMetricObservation>()));
        BacktestResult expected = new BacktestEngine().Run(input, full);
        BacktestResult actual = new BacktestEngine().Run(input, narrow, options: new BacktestRunOptions { RetainLedger = false });
        observations.Count.ShouldBe(snapshots.Count);

        for (var index = 0; index < observations.Count; index++)
        {
            CandleMetricObservation observation = observations[index];
            CandleSnapshot snapshot = snapshots[index];
            observation.Sequence.ShouldBe(snapshot.Sequence);
            observation.CandleIndex.ShouldBe(snapshot.Context.CandleIndex);
            observation.Equity.ShouldBe(snapshot.Equity);
            observation.UnrealizedPnl.ShouldBe(snapshot.UnrealizedPnl);
            observation.RealizedPnl.ShouldBe(snapshot.Account.RealizedPnl);
            observation.FeesBase.ShouldBe(snapshot.Account.FeesBase);
            observation.FeesQuote.ShouldBe(snapshot.Account.FeesQuote);
        }

        narrow.DidNotReceive().OnCandleProcessed(Arg.Any<CandleSnapshot>());
        narrow.Received(expected.Ledger.Count).OnLedger(Arg.Any<BacktestLedgerEntry>());
        narrow.Received(1).OnTerminal(Arg.Any<BacktestTerminalSnapshot>());
        JsonSerializer.Serialize(actual.Terminal).ShouldBe(JsonSerializer.Serialize(expected.Terminal));
    }

    [Test]
    public void NarrowObserverPreservesValuationOverflowCursorAndDiagnostic()
    {
        BacktestInput input = Input(StartBuy(), [Bar(index: 0), Bar(index: 1, high: decimal.MaxValue, close: decimal.MaxValue)]);
        var observer = Substitute.For<IBacktestMetricsObserver>();
        BacktestResult expected = new BacktestEngine().Run(input);
        BacktestResult actual = new BacktestEngine().Run(input, observer, options: new BacktestRunOptions { RetainLedger = false });
        actual.Terminal.Outcome.ShouldBe(BacktestOutcome.Failed);
        observer.Received(1).OnCandleMetrics(Arg.Any<CandleMetricObservation>());
        observer.Received(1).OnDiagnostic(Arg.Is<BacktestDiagnostic>(diagnostic => diagnostic != null && diagnostic.Code == "ValuationOverflow"));
        observer.Received(1).OnTerminal(Arg.Any<BacktestTerminalSnapshot>());
        JsonSerializer.Serialize(actual.Terminal).ShouldBe(JsonSerializer.Serialize(expected.Terminal));
    }

    private static BacktestResult Execute(
        BacktestInput input,
        BacktestOutcome outcome,
        bool retainLedger,
        out BacktestLedgerEntry[] received,
        out CandleSnapshot[] candles)
    {
        using var source = new CancellationTokenSource();
        var observer = Substitute.For<IBacktestObserver>();
        var facts = new List<BacktestLedgerEntry>();
        var snapshots = new List<CandleSnapshot>();
        observer.When(value => value.OnCandleProcessed(Arg.Any<CandleSnapshot>())).Do(call => snapshots.Add(call.Arg<CandleSnapshot>()!));
        observer.When(value => value.OnLedger(Arg.Any<BacktestLedgerEntry>()))
            .Do(call =>
            {
                var entry = call.Arg<BacktestLedgerEntry>()!;
                facts.Add(entry);

                if (entry.Kind != BacktestLedgerKind.Filled)
                {
                    return;
                }

                if (outcome == BacktestOutcome.Cancelled)
                {
                    source.Cancel();
                }
                else if (outcome == BacktestOutcome.Failed)
                {
                    throw new InvalidOperationException("Fixture observer failure.");
                }
            });
        BacktestResult result = new BacktestEngine().Run(input, observer, source.Token, new BacktestRunOptions { RetainLedger = retainLedger });
        received = facts.ToArray();
        candles = snapshots.ToArray();

        return result;
    }
}