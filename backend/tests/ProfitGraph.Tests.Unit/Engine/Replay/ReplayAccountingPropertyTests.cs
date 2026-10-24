using NSubstitute;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Replay;

using static ProfitGraph.Tests.Unit.Engine.Replay.ContractFixtureTests;
using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class ReplayAccountingPropertyTests
{
    [Test]
    public void GeneratedPositiveHistoriesReconcileEverySnapshotAndCommittedFill()
    {
        for (var seed = 1; seed <= 40; seed++)
        {
            var graph = new StrategyGraph(
                [
                    Buy("buy", mode: ActionEvaluationMode.OnCandleClose), TradingNode("balance", "Balance"),
                    TradingNode("sell", "MarketSell", new Dictionary<string, ParamValue> { ["tag"] = Text("sell") }, ActionEvaluationMode.OnEvent)
                ],
                [Edge("buy", "filled", "sell", "trigger"), Edge("balance", "baseAvailable", "sell", "quantity")]);
            var snapshots = new List<CandleSnapshot>();
            var observer = Substitute.For<IBacktestObserver>();
            observer.When(value => value.OnCandleProcessed(Arg.Any<CandleSnapshot>())).Do(call => snapshots.Add(call.Arg<CandleSnapshot>()!));
            BacktestResult result = Run(
                graph,
                Enumerable.Range(start: 0, count: 12).Select(index => Bar(index, 50m + index * seed % 100)),
                seed / 10000m,
                seed / 100000m,
                observer: observer);
            result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);

            foreach (CandleSnapshot snapshot in snapshots)
            {
                snapshot.Account.QuoteAvailable.ShouldBeGreaterThanOrEqualTo(0m);
                snapshot.Account.BaseAvailable.ShouldBeGreaterThanOrEqualTo(0m);
                snapshot.TotalPnl.ShouldBe(snapshot.Account.RealizedPnl + snapshot.UnrealizedPnl);
            }

            Fill[] fills = result.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled).Select(entry => entry.Fill!).ToArray();
            result.Terminal.Account.QuoteTotal.ShouldBe(1000m + fills.Sum(fill => fill.QuoteChange));
            result.Terminal.Account.BaseTotal.ShouldBe(fills.Sum(fill => fill.BaseChange));
            result.Terminal.Account.FeesBase.ShouldBe(fills.Where(fill => fill.FeeAsset == FeeAsset.Base).Sum(fill => fill.Fee));
            result.Terminal.Account.FeesQuote.ShouldBe(fills.Where(fill => fill.FeeAsset == FeeAsset.Quote).Sum(fill => fill.Fee));
            result.Terminal.Account.QuoteReserved.ShouldBe(0m);
            result.Terminal.Account.BaseReserved.ShouldBe(0m);
        }
    }
}