using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Positions;

namespace ProfitGraph.Tests.Unit.Domain.Bots.Positions;

/// <summary>
///     A position cycle runs from the fill that opened a position to the fill that made it flat (ADR 0021).
/// </summary>
public sealed class BotPositionCycleTests
{
    private static readonly DateTimeOffset Opened = BotFixture.Created;
    private static readonly DateTimeOffset Closed = BotFixture.Created.AddHours(3);

    [Test]
    public void OpeningRecordsTheRealizedProfitSoFar()
    {
        var id = BotPositionCycleId.New();
        var botId = BotId.New();

        BotPositionCycle cycle = BotPositionCycle.Open(id, botId, Opened, realizedPnl: 12.5m);

        cycle.ShouldSatisfyAllConditions(
            () => cycle.RealizedPnlAtOpen.ShouldBe(12.5m),
            () => cycle.ClosedAt.ShouldBeNull(),
            () => cycle.RealizedPnl.ShouldBeNull(),
            () => cycle.DomainEvents.ShouldBe([new BotPositionOpened(botId, id, Opened)]));
    }

    [Test]
    public void ClosingRealizesTheProfitOfTheCycleOnly()
    {
        BotPositionCycle cycle = BotPositionCycle.Open(BotPositionCycleId.New(), BotId.New(), Opened, realizedPnl: 12.5m);
        cycle.ClearDomainEvents();

        cycle.Close(Closed, realizedPnl: 10m).ShouldBeTrue();

        cycle.ShouldSatisfyAllConditions(
            () => cycle.ClosedAt.ShouldBe(Closed),
            () => cycle.RealizedPnl.ShouldBe(-2.5m),
            () => cycle.DomainEvents.ShouldBe([new BotPositionClosed(cycle.BotId, cycle.Id, Closed, RealizedPnl: -2.5m)]));
    }

    [Test]
    public void AClosedCycleIsNotClosedAgain()
    {
        BotPositionCycle cycle = BotPositionCycle.Open(BotPositionCycleId.New(), BotId.New(), Opened, realizedPnl: 0m);
        cycle.Close(Closed, realizedPnl: 4m);
        cycle.ClearDomainEvents();

        cycle.Close(Closed.AddHours(1), realizedPnl: 9m).ShouldBeFalse();

        cycle.ShouldSatisfyAllConditions(
            () => cycle.ClosedAt.ShouldBe(Closed),
            () => cycle.RealizedPnl.ShouldBe(4m),
            () => cycle.DomainEvents.ShouldBeEmpty());
    }
}