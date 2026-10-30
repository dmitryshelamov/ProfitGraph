using System.Collections.Immutable;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotPosition;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.Fills;
using ProfitGraph.Domain.Bots.MarkPrices;
using ProfitGraph.Domain.Bots.Positions;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Unit.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Application.Bots;

/// <summary>
///     The bot's position as its card shows it (ADR 0063): a short's cost basis is what it sold for less the fees (ADR 0061), so its
///     average entry price adds the fees back, and its profit counts them as spent when it sold.
/// </summary>
public sealed class BotPositionTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created;

    [Test]
    public async Task AShortReportsTheAveragePriceItSoldAtAndItsProfitWithTheFeeSpent()
    {
        // Sold 10 at 100 for 1000 with a fee of 1: the cost basis is the net proceeds, 999.
        BotPositionInternal position = await Position(
            baseTotal: -10m,
            quoteTotal: 1999m,
            costBasis: 999m,
            feesInCost: 1m,
            close: 90m);

        position.BaseTotal.ShouldBe(-10m);
        position.EntryFees.ShouldBe(1m);
        position.AverageCost.ShouldBe(99.9m);
        position.AverageBuyPrice.ShouldBe(100m);
        position.RealizedPnl.ShouldBe(-1m);
        position.UnrealizedPnl.ShouldBe(100m);
    }

    [Test]
    public async Task ALongReportsTheAveragePriceItBoughtAtWithoutTheFee()
    {
        // Bought 10 at 100 for 1000 with a fee of 1: the cost basis is what it paid, 1001.
        BotPositionInternal position = await Position(
            baseTotal: 10m,
            quoteTotal: -1m,
            costBasis: 1001m,
            feesInCost: 1m,
            close: 110m);

        position.AverageCost.ShouldBe(100.1m);
        position.AverageBuyPrice.ShouldBe(100m);
        position.RealizedPnl.ShouldBe(-1m);
        position.UnrealizedPnl.ShouldBe(100m);
    }

    [Test]
    public async Task AFlatPositionHasNoAveragePrice()
    {
        BotPositionInternal position = await Position(
            baseTotal: 0m,
            quoteTotal: 1000m,
            costBasis: 0m,
            feesInCost: 0m,
            close: 100m);

        position.AverageCost.ShouldBeNull();
        position.AverageBuyPrice.ShouldBeNull();
        position.UnrealizedPnl.ShouldBe(0m);
    }

    private static async Task<BotPositionInternal> Position(decimal baseTotal, decimal quoteTotal, decimal costBasis, decimal feesInCost, decimal close)
    {
        Bot bot = BotFixture.Running();
        var bots = Substitute.For<IBotRepository>();
        bots.Get(bot.Id, Arg.Any<CancellationToken>()).Returns(bot);
        var checkpoints = Substitute.For<IBotCheckpointRepository>();
        checkpoints.Get(bot.Id, Arg.Any<CancellationToken>()).Returns(Checkpoint(bot, baseTotal, quoteTotal, costBasis, feesInCost, close));
        var cycles = Substitute.For<IBotPositionCycleRepository>();
        cycles.List(bot.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        var fills = Substitute.For<IBotFillRepository>();
        fills.QuoteFees(bot.Id, Arg.Any<IReadOnlyList<BotFillRange>>(), Arg.Any<CancellationToken>()).Returns([]);
        var prices = Substitute.For<ILiveMarkPriceRepository>();
        var handler = new GetBotPositionQueryHandler(bots, checkpoints, cycles, fills, prices, new FakeTimeProvider(Now));

        return await handler.Handle(new GetBotPositionQuery(bot.OwnerId.Value, bot.Id.Value, CycleLimit: 5), CancellationToken.None);
    }

    private static BotCheckpoint Checkpoint(Bot bot, decimal baseTotal, decimal quoteTotal, decimal costBasis, decimal feesInCost, decimal close)
    {
        var account = new ExternalAccountCheckpoint(
            StartingCapital: 1000m,
            quoteTotal,
            QuoteReserved: 0m,
            baseTotal,
            BaseReserved: 0m,
            costBasis,
            RealizedPnl: 0m,
            FeesBase: 0m,
            feesInCost,
            LastBuyPrice: null,
            NextOrderId: 1,
            [],
            [],
            FeesInCost: feesInCost);
        var engine = new GraphEngineCheckpoint(
            account,
            NextEventId: 1,
            NextAttemptId: 0,
            ImmutableDictionary<long, long>.Empty,
            [],
            new Candle(Now.AddHours(-1), close, close, close, close, Volume: 0m),
            LastClosedCandleIndex: 0,
            Now,
            Started: true,
            "v1",
            ImmutableDictionary<long, string>.Empty);

        return new BotCheckpoint(bot.Id, engine, bot.Parameters, Now);
    }
}