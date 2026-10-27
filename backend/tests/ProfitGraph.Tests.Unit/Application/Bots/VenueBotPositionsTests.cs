using System.Collections.Immutable;

using NSubstitute;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetVenueBotPositions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Unit.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Application.Bots;

/// <summary>
///     The positions a venue module adds up per account before it closes an order the exchange ended (ADR 0031): every bot of
///     the venue, whatever its state, with the base total of its last checkpoint, and nothing for a bot that never saved one.
/// </summary>
public sealed class VenueBotPositionsTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created;

    [Test]
    public async Task EveryBotOfTheVenueIsListedWithItsCheckpointsBaseTotal_AndABotWithoutOneHoldsNothing()
    {
        Bot running = BotFixture.Running();
        Bot stopped = BotFixture.DegradedBy(BotStatusReason.UnresolvedOrder);
        var bots = Substitute.For<IBotRepository>();
        bots.ListOnVenue(running.Series.Venue, Arg.Any<CancellationToken>()).Returns([running, stopped]);
        var checkpoints = Substitute.For<IBotCheckpointRepository>();
        checkpoints.Get(running.Id, Arg.Any<CancellationToken>()).Returns(Checkpoint(running, baseTotal: 30m));
        checkpoints.Get(stopped.Id, Arg.Any<CancellationToken>()).Returns((BotCheckpoint?)null);

        IReadOnlyList<VenueBotPositionInternal> positions = await new GetVenueBotPositionsQueryHandler(bots, checkpoints).Handle(
            new GetVenueBotPositionsQuery(running.Series.Venue.Value),
            CancellationToken.None);

        positions.ShouldBe(
        [
            new VenueBotPositionInternal(running.Id.Value, running.Series.Symbol.Value, BaseTotal: 30m),
            new VenueBotPositionInternal(stopped.Id.Value, stopped.Series.Symbol.Value, BaseTotal: 0m)
        ]);
    }

    private static BotCheckpoint Checkpoint(Bot bot, decimal baseTotal)
    {
        var account = new ExternalAccountCheckpoint(
            StartingCapital: 1000m,
            QuoteTotal: 1000m,
            QuoteReserved: 0m,
            baseTotal,
            BaseReserved: 0m,
            CostBasis: 0m,
            RealizedPnl: 0m,
            FeesBase: 0m,
            FeesQuote: 0m,
            LastBuyPrice: null,
            NextOrderId: 1,
            [],
            []);
        var engine = new GraphEngineCheckpoint(
            account,
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

        return new BotCheckpoint(bot.Id, engine, bot.Parameters, Now);
    }
}