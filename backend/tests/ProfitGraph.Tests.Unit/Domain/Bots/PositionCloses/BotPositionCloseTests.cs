using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.PositionCloses;

namespace ProfitGraph.Tests.Unit.Domain.Bots.PositionCloses;

/// <summary>
///     The owner's position close (ADR 0064): pending, cancelling the bot's orders, selling the position, and closed or failed.
/// </summary>
public sealed class BotPositionCloseTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created;

    [Test]
    public void ANewCloseIsPendingAndActive()
    {
        BotPositionClose close = Close();

        close.Status.ShouldBe(BotPositionCloseStatus.Pending);
        close.IsActive.ShouldBeTrue();
        close.ResolvedAt.ShouldBeNull();
        close.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void AStartCancelsTheOrdersAndIsReported()
    {
        BotPositionClose close = Close();

        close.Start(cancelledOrders: 2, Now);

        close.Status.ShouldBe(BotPositionCloseStatus.Cancelling);
        close.CancelledOrders.ShouldBe(2);
        close.DomainEvents.ShouldHaveSingleItem().ShouldBe(new BotPositionCloseStarted(close.BotId, close.Id, CancelledOrders: 2, StopAfter: true, Now));
    }

    [Test]
    public void ASaleThatEmptiesThePositionClosesIt()
    {
        BotPositionClose close = Close();
        close.Start(cancelledOrders: 0, Now);

        close.Sell(engineOrderId: 7, Now);
        close.Status.ShouldBe(BotPositionCloseStatus.Selling);
        close.EngineOrderId.ShouldBe(7);
        close.Close(Now.AddSeconds(5));

        close.Status.ShouldBe(BotPositionCloseStatus.Closed);
        close.IsActive.ShouldBeFalse();
        close.ResolvedAt.ShouldBe(Now.AddSeconds(5));
        close.DomainEvents.Last().ShouldBe(new BotPositionCloseEnded(close.BotId, close.Id, BotPositionCloseStatus.Closed, Reason: null, Now.AddSeconds(5)));
    }

    [Test]
    public void AFailedCloseKeepsItsReason()
    {
        BotPositionClose close = Close();
        close.Start(cancelledOrders: 1, Now);

        close.Fail("InsufficientBase", Now);

        close.Status.ShouldBe(BotPositionCloseStatus.Failed);
        close.FailureReason.ShouldBe("InsufficientBase");
        Should.Throw<BotRuleException>(() => close.Close(Now));
        Should.Throw<BotRuleException>(() => close.Fail("again", Now));
    }

    [Test]
    public void StepsOutOfOrderAreRefused()
    {
        BotPositionClose close = Close();

        Should.Throw<BotRuleException>(() => close.Sell(engineOrderId: 1, Now));
        Should.Throw<BotRuleException>(() => close.Close(Now));
        close.Start(cancelledOrders: 0, Now);
        Should.Throw<BotRuleException>(() => close.Start(cancelledOrders: 0, Now));
    }

    [Test]
    public void ACloseNobodyTookExpires()
    {
        BotPositionClose close = Close();

        close.Expire(Now.AddSeconds(30)).ShouldBeFalse();
        close.Expire(Now + BotPositionClose.Lifetime + TimeSpan.FromSeconds(1)).ShouldBeTrue();

        close.Status.ShouldBe(BotPositionCloseStatus.Failed);
        close.FailureReason.ShouldBe(BotPositionClose.ExpiredReason);
    }

    [Test]
    public void AStoredCloseIsRestoredOnlyWhenConsistent()
    {
        BotPositionClose restored = BotPositionClose.Restore(
            BotPositionCloseId.New(),
            BotId.New(),
            stopAfter: false,
            BotPositionCloseStatus.Selling,
            cancelledOrders: 1,
            engineOrderId: 3,
            failureReason: null,
            Now,
            resolvedAt: null);

        restored.IsActive.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => BotPositionClose.Restore(
            BotPositionCloseId.New(),
            BotId.New(),
            stopAfter: false,
            BotPositionCloseStatus.Selling,
            cancelledOrders: 1,
            engineOrderId: null,
            failureReason: null,
            Now,
            resolvedAt: null));
    }

    private static BotPositionClose Close()
    {
        return BotPositionClose.Create(BotPositionCloseId.New(), BotId.New(), stopAfter: true, Now);
    }
}