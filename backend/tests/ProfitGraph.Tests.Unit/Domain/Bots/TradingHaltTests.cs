using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Domain.Bots;

public sealed class TradingHaltTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created;

    [Test]
    public void HaltIsRecordedOnce()
    {
        TradingHalt halt = TradingHalt.Restore(isHalted: false, Now);

        halt.Halt(Now).ShouldBeTrue();
        halt.Halt(Now.AddMinutes(1)).ShouldBeFalse();

        halt.IsHalted.ShouldBeTrue();
        halt.UpdatedAt.ShouldBe(Now);
        halt.DomainEvents.ShouldBe([new TradingHalted(Now)]);
    }

    [Test]
    public void ResumeOnlyClearsAnActiveHalt()
    {
        TradingHalt halt = TradingHalt.Restore(isHalted: true, Now);

        halt.Resume(Now).ShouldBeTrue();
        halt.Resume(Now).ShouldBeFalse();

        halt.IsHalted.ShouldBeFalse();
        halt.DomainEvents.ShouldBe([new TradingResumed(Now)]);
    }
}