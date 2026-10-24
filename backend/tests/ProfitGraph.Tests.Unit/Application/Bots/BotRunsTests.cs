using System.Collections.Immutable;

using ProfitGraph.Application.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Unit.Application.Bots;

public sealed class BotRunsTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void RunWithoutOrdersOrHoldingsIsUntraded()
    {
        BotRuns.IsUntraded(Checkpoint(nextOrderId: 0, baseTotal: 0m)).ShouldBeTrue();
    }

    [Test]
    public void RunThatCreatedAnOrderOrHoldsBaseIsTraded()
    {
        BotRuns.IsUntraded(Checkpoint(nextOrderId: 1, baseTotal: 0m)).ShouldBeFalse();
        BotRuns.IsUntraded(Checkpoint(nextOrderId: 0, baseTotal: 0.1m)).ShouldBeFalse();
    }

    private static GraphEngineCheckpoint Checkpoint(long nextOrderId, decimal baseTotal)
    {
        return new GraphEngineCheckpoint(
            new ExternalAccountCheckpoint(
                StartingCapital: 100m,
                QuoteTotal: 100m,
                QuoteReserved: 0m,
                baseTotal,
                BaseReserved: 0m,
                CostBasis: 0m,
                RealizedPnl: 0m,
                FeesBase: 0m,
                FeesQuote: 0m,
                LastBuyPrice: null,
                nextOrderId,
                [],
                []),
            NextEventId: 1,
            NextAttemptId: 0,
            ImmutableDictionary<long, long>.Empty,
            [],
            new Candle(Now.AddHours(-1), Open: 1m, High: 1m, Low: 1m, Close: 1m, Volume: 0m),
            LastClosedCandleIndex: 0,
            Now,
            Started: true,
            "v1",
            ImmutableDictionary<long, string>.Empty);
    }
}