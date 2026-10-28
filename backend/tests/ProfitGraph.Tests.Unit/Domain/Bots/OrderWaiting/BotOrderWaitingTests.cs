using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderWaiting;
using ProfitGraph.Domain.Common;

namespace ProfitGraph.Tests.Unit.Domain.Bots.OrderWaiting;

/// <summary>
///     A working order its venue holds locally (ADR 0023, TC4): a new reason ends the current wait and starts a new one,
///     new details of the same reason are not reported, and the end is reported once.
/// </summary>
public sealed class BotOrderWaitingTests
{
    private static readonly DateTimeOffset At = new DateTimeOffset(year: 2026, month: 10, day: 27, hour: 10, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void AWaitStartsWithItsReason_AndIsReported()
    {
        BotOrderWaiting waiting = BotOrderWaiting.Start(BotId.New(), engineOrderId: 3, OrderWaitingCode.PriceBand, At, until: null, low: 90m, high: 110m);

        waiting.ShouldSatisfyAllConditions(
            () => waiting.Code.ShouldBe(OrderWaitingCode.PriceBand),
            () => waiting.Since.ShouldBe(At),
            () => waiting.Low.ShouldBe(90m),
            () => waiting.High.ShouldBe(110m));
        waiting.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<BotOrderWaitingStarted>().Code.ShouldBe(OrderWaitingCode.PriceBand);
    }

    [Test]
    public void NewBoundsOfTheSameReasonChangeQuietly_AndANewReasonEndsTheOldWaitFirst()
    {
        BotOrderWaiting waiting = BotOrderWaiting.Start(BotId.New(), engineOrderId: 3, OrderWaitingCode.PriceBand, At, until: null, low: 90m, high: 110m);
        waiting.ClearDomainEvents();

        waiting.Change(OrderWaitingCode.PriceBand, At.AddMinutes(1), until: null, low: 90m, high: 110m).ShouldBeFalse();
        waiting.Change(OrderWaitingCode.PriceBand, At.AddMinutes(2), until: null, low: 91m, high: 111m).ShouldBeTrue();
        waiting.DomainEvents.ShouldBeEmpty();
        waiting.Since.ShouldBe(At);

        waiting.Change(OrderWaitingCode.CarryOver, At.AddHours(9), until: null, low: null, high: null).ShouldBeTrue();

        waiting.Since.ShouldBe(At.AddHours(9));
        waiting.Low.ShouldBeNull();
        IDomainEvent[] events = [.. waiting.DomainEvents];
        events.Length.ShouldBe(2);
        events[0].ShouldBeOfType<BotOrderWaitingEnded>().Code.ShouldBe(OrderWaitingCode.PriceBand);
        events[1].ShouldBeOfType<BotOrderWaitingStarted>().Code.ShouldBe(OrderWaitingCode.CarryOver);
    }

    [Test]
    public void TheEndIsReported()
    {
        BotOrderWaiting waiting = BotOrderWaiting.Start(BotId.New(), engineOrderId: 3, OrderWaitingCode.SessionClosed, At, until: null, low: null, high: null);
        waiting.ClearDomainEvents();

        waiting.End(At.AddHours(1));

        waiting.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<BotOrderWaitingEnded>().Code.ShouldBe(OrderWaitingCode.SessionClosed);
    }

    [Test]
    public void BoundsArePositiveAndOrdered_AndAnEndFollowsTheStart()
    {
        Should.Throw<ArgumentException>(() => BotOrderWaiting.Start(BotId.New(), engineOrderId: 3, OrderWaitingCode.PriceBand, At, until: null, low: 110m, high: 90m));
        Should.Throw<ArgumentException>(() => BotOrderWaiting.Start(BotId.New(), engineOrderId: 3, OrderWaitingCode.PriceBand, At, until: null, low: 0m, high: 90m));
        Should.Throw<ArgumentException>(() => BotOrderWaiting.Start(
            BotId.New(),
            engineOrderId: 3,
            OrderWaitingCode.SettlementHold,
            At,
            At.AddMinutes(-1),
            low: null,
            high: null));
    }
}