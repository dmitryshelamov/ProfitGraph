using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Domain.Bots;

/// <summary>
///     Settings, graph versions and diagnostics of a bot.
/// </summary>
public sealed class BotSettingsTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created.AddMinutes(5);

    [Test]
    public void StoppedBotChangesParametersAndAllocation()
    {
        Bot bot = BotFixture.Stopped();
        var parameters = new BotParameters(new Dictionary<string, decimal> { ["drop"] = 0.03m });

        bot.ChangeParameters(parameters, allocationCap: 150m, Now);

        bot.ShouldSatisfyAllConditions(
            () => bot.Parameters.ShouldBe(parameters),
            () => bot.AllocationCap.ShouldBe(150m),
            () => bot.DomainEvents.ShouldBe([new BotParametersChanged(bot.Id, Now)]));
    }

    [Test]
    public void ParametersChangeOnlyWhenTheRuntimeStopped()
    {
        Bot running = BotFixture.Running();
        Bot stopping = BotFixture.Running();
        stopping.Stop(Now);

        Should.Throw<BotRuleException>(() => running.ChangeParameters(running.Parameters, allocationCap: 50m, Now)).Code.ShouldBe(BotRuleException.MustBeStopped);
        Should.Throw<BotRuleException>(() => stopping.ChangeParameters(stopping.Parameters, allocationCap: 50m, Now)).Code.ShouldBe(BotRuleException.MustBeStopped);
    }

    [Test]
    public void ValuesFollowTheParametersOfTheVersionTheBotRunsEvenWhileRunning()
    {
        Bot bot = BotFixture.Running();
        StrategyParameter[] parameters = [new StrategyParameter("buyAmount", ValueType.Scalar, DefaultValue: 50m)];

        bot.FitParameters(parameters, Now).ShouldBeTrue();

        bot.ShouldSatisfyAllConditions(
            () => bot.Parameters.ShouldBe(new BotParameters(new Dictionary<string, decimal> { ["buyAmount"] = 50m })),
            () => bot.UpdatedAt.ShouldBe(Now),
            () => bot.DomainEvents.ShouldHaveSingleItem()
                .ShouldBeOfType<BotParametersFitted>()
                .ShouldSatisfyAllConditions(
                    fitted => fitted.Added.ShouldBe(new Dictionary<string, decimal> { ["buyAmount"] = 50m }),
                    fitted => fitted.Removed.ShouldBe(new[] { "drop" }),
                    fitted => fitted.At.ShouldBe(Now)));
    }

    [Test]
    public void ValuesThatFitTheVersionStayAndRaiseNothing()
    {
        Bot bot = BotFixture.Running();

        bot.FitParameters([new StrategyParameter("drop", ValueType.Scalar, DefaultValue: 0.5m)], Now).ShouldBeFalse();

        bot.Parameters.Values["drop"].ShouldBe(0.02m);
        bot.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void FittedValuesKeepKnownValuesTakeDefaultsForNewParametersAndDropRemovedOnes()
    {
        var values = new BotParameters(new Dictionary<string, decimal> { ["drop"] = 0.02m, ["rise"] = 0.1m });

        BotParameters fitted = values.Fit(
            [new StrategyParameter("drop", ValueType.Scalar, DefaultValue: 0.5m), new StrategyParameter("levels", ValueType.Integer, DefaultValue: 3m)]);

        fitted.Values.ShouldBe(new Dictionary<string, decimal> { ["drop"] = 0.02m, ["levels"] = 3m }, ignoreOrder: true);
    }

    [Test]
    public void RiskLimitsChangeWhileRunning()
    {
        Bot bot = BotFixture.Running();
        var limits = new BotRiskLimits(maxOrderQuote: 30m, dailyLossLimitQuote: 15m);

        bot.ChangeRiskLimits(limits, Now);
        bot.ChangeRiskLimits(new BotRiskLimits(maxOrderQuote: 30m, dailyLossLimitQuote: 15m), Now);

        bot.RiskLimits.ShouldBe(limits);
        bot.DomainEvents.ShouldBe([new BotRiskLimitsChanged(bot.Id, Now)]);
    }

    [TestCase(arg1: 0, arg2: 1)]
    [TestCase(arg1: 1, arg2: 0)]
    [TestCase(arg1: -1, arg2: 1)]
    public void RiskLimitsMustBePositive(decimal maxOrder, decimal dailyLoss)
    {
        Should.Throw<BotRuleException>(() => new BotRiskLimits(maxOrder, dailyLoss)).Code.ShouldBe(BotRuleException.InvalidSettings);
    }

    [Test]
    public void ForeignOrdersPolicyChangesOnlyWhenStopped()
    {
        Bot stopped = BotFixture.Stopped();
        Bot running = BotFixture.Running();

        stopped.ChangeForeignOrdersPolicy(allowForeignOrders: true, Now);
        stopped.ChangeForeignOrdersPolicy(allowForeignOrders: true, Now);

        stopped.AllowForeignOrders.ShouldBeTrue();
        stopped.DomainEvents.ShouldBe([new BotForeignOrdersPolicyChanged(stopped.Id, AllowForeignOrders: true, Now)]);
        Should.Throw<BotRuleException>(() => running.ChangeForeignOrdersPolicy(allowForeignOrders: true, Now)).Code.ShouldBe(BotRuleException.MustBeStopped);
    }

    [Test]
    public void FeeRatesRaiseAnEventOnlyWhenTheyDiffer()
    {
        Bot bot = BotFixture.Running();

        bot.ChangeFeeRates(new BotFeeRates(limitRate: 0.001m, marketRate: 0.001m, Now), Now);
        bot.DomainEvents.ShouldBeEmpty();
        bot.FeeRates.ReadAt.ShouldBe(Now);

        bot.ChangeFeeRates(new BotFeeRates(limitRate: 0.0008m, marketRate: 0.001m, Now), Now);
        bot.DomainEvents.ShouldBe([new BotFeeRatesChanged(bot.Id, Now)]);
    }

    [TestCase(-0.0001)]
    [TestCase(1)]
    [TestCase(0.0000000000001)]
    public void FeeRateIsAFractionWithAtMostTwelveDecimals(decimal rate)
    {
        Should.Throw<BotRuleException>(() => new BotFeeRates(rate, marketRate: 0.001m, Now));
    }

    [Test]
    public void AnUnresolvedOrderIsClearedOnlyForABotThatShouldRun()
    {
        Bot stopped = BotFixture.Running();
        stopped.Degrade(BotStatusReason.UnresolvedOrder, Now).ShouldBeTrue();
        stopped.Stop(Now);
        Bot unresolved = BotFixture.Running();
        unresolved.Degrade(BotStatusReason.UnresolvedOrder, Now, "AutoCheck:TradingDayNotEnded").ShouldBeTrue();
        Bot foreign = BotFixture.DegradedBy(BotStatusReason.ForeignOrder);
        Bot healthy = BotFixture.Running();
        unresolved.ClearDomainEvents();

        unresolved.ClearUnresolvedOrder(Now).ShouldBeTrue();
        stopped.ClearUnresolvedOrder(Now).ShouldBeFalse();
        foreign.ClearUnresolvedOrder(Now).ShouldBeFalse();
        healthy.ClearUnresolvedOrder(Now).ShouldBeFalse();

        unresolved.ShouldSatisfyAllConditions(
            () => unresolved.RuntimeStatus.ShouldBe(BotRuntimeStatus.Created),
            () => unresolved.StatusReason.ShouldBeNull(),
            () => unresolved.DesiredState.ShouldBe(BotDesiredState.Running),
            () => unresolved.Diagnostics.ShouldBe(BotDiagnostics.None),
            () => unresolved.DomainEvents.ShouldBe([new BotRecovered(unresolved.Id, Now)]));
        stopped.StatusReason.ShouldBe(BotStatusReason.UnresolvedOrder);
        stopped.DesiredState.ShouldBe(BotDesiredState.Stopped);
        foreign.StatusReason.ShouldBe(BotStatusReason.ForeignOrder);
    }

    [Test]
    public void SellerFeesReplaceTheRatesAndCountAsAChange()
    {
        var schedule = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m);
        Bot bot = BotFixture.Running();

        Should.Throw<BotRuleException>(() => new BotFeeRates(limitRate: 0.001m, marketRate: 0m, Now, schedule)).Code.ShouldBe(BotRuleException.InvalidSettings);
        bot.ChangeFeeRates(new BotFeeRates(limitRate: 0m, marketRate: 0m, Now, schedule), Now);
        bot.FeeRates.SellerFees.ShouldBe(schedule);
        bot.DomainEvents.ShouldBe([new BotFeeRatesChanged(bot.Id, Now)]);

        bot.ClearDomainEvents();
        bot.ChangeFeeRates(new BotFeeRates(limitRate: 0m, marketRate: 0m, Now, new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m)), Now);
        bot.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void ResumingAVenueAccountReArmsOnlyTheBotsItStopped()
    {
        Bot held = BotFixture.Running();
        held.Degrade(BotStatusReason.VenueAccountProtection, Now).ShouldBeTrue();
        Bot otherwise = BotFixture.DegradedBy(BotStatusReason.KeyPermissions);
        held.ClearDomainEvents();

        held.ClearVenueAccountProtection(Now).ShouldBeTrue();
        otherwise.ClearVenueAccountProtection(Now).ShouldBeFalse();

        held.RuntimeStatus.ShouldBe(BotRuntimeStatus.Created);
        held.StatusReason.ShouldBeNull();
        held.DomainEvents.ShouldBe([new BotRecovered(held.Id, Now)]);
        otherwise.StatusReason.ShouldBe(BotStatusReason.KeyPermissions);
        BotStatusReasons.IsSafety(BotStatusReason.VenueAccountProtection).ShouldBeTrue();
    }

    [Test]
    public void FeeRatesNeedAUtcReadTime()
    {
        Should.Throw<BotRuleException>(() => new BotFeeRates(limitRate: 0.001m, marketRate: 0.001m, new DateTimeOffset(Now.DateTime, TimeSpan.FromHours(3))));
    }

    [Test]
    public void ParameterNamesAreRequiredAndCompareByValue()
    {
        Should.Throw<BotRuleException>(() => new BotParameters(new Dictionary<string, decimal> { [" "] = 1m }));
        new BotParameters(new Dictionary<string, decimal> { ["a"] = 1m, ["b"] = 2m })
            .ShouldBe(new BotParameters(new Dictionary<string, decimal> { ["b"] = 2m, ["a"] = 1m }));
        new BotParameters(new Dictionary<string, decimal> { ["a"] = 1m })
            .ShouldNotBe(new BotParameters(new Dictionary<string, decimal> { ["a"] = 1.5m }));
    }

    [Test]
    public void AppliedVersionRaisesAnEventAndClearsARejection()
    {
        Bot bot = BotFixture.Running();
        var rejected = StrategyVersionId.New();
        var applied = StrategyVersionId.New();
        bot.RejectStrategyVersion(rejected, Now);
        bot.ClearDomainEvents();

        bot.ApplyStrategyVersion(applied, Now);

        bot.ShouldSatisfyAllConditions(
            () => bot.StrategyVersionId.ShouldBe(applied),
            () => bot.Diagnostics.HasFlag(BotDiagnostics.GraphChangeRejected).ShouldBeFalse(),
            () => bot.DomainEvents.ShouldBe([new BotGraphVersionApplied(bot.Id, applied, Now)]));
    }

    [Test]
    public void ApplyingTheCurrentVersionRaisesNothingAndKeepsARejection()
    {
        Bot bot = BotFixture.Running();
        bot.RejectStrategyVersion(StrategyVersionId.New(), Now);
        bot.ClearDomainEvents();

        bot.ApplyStrategyVersion(BotFixture.FirstVersion, Now);

        bot.DomainEvents.ShouldBeEmpty();
        bot.Diagnostics.HasFlag(BotDiagnostics.GraphChangeRejected).ShouldBeTrue();
    }

    [Test]
    public void TheSameRejectedVersionIsReportedOnce()
    {
        Bot bot = BotFixture.Running();
        var candidate = StrategyVersionId.New();

        bot.RejectStrategyVersion(candidate, Now).ShouldBeTrue();
        bot.RejectStrategyVersion(candidate, Now).ShouldBeFalse();
        bot.RejectStrategyVersion(StrategyVersionId.New(), Now).ShouldBeTrue();

        bot.DomainEvents.Count.ShouldBe(2);
        bot.StrategyVersionId.ShouldBe(BotFixture.FirstVersion);
        bot.Diagnostics.HasFlag(BotDiagnostics.GraphChangeRejected).ShouldBeTrue();
    }

    [Test]
    public void StaleFeedTogglesWithAnEventEachWay()
    {
        Bot bot = BotFixture.Running();

        bot.MarkFeedStale(stale: true, Now).ShouldBeTrue();
        bot.MarkFeedStale(stale: true, Now).ShouldBeFalse();
        bot.MarkFeedStale(stale: false, Now).ShouldBeTrue();

        bot.Diagnostics.ShouldBe(BotDiagnostics.None);
        bot.DomainEvents.ShouldBe([new BotFeedStalled(bot.Id, Now), new BotFeedRecovered(bot.Id, Now)]);
    }

    [Test]
    public void StoppedBotHasNoStaleFeed()
    {
        BotFixture.Stopped().MarkFeedStale(stale: true, Now).ShouldBeFalse();
    }

    [Test]
    public void OnlyTheFirstUnavailableReportOfAStreakRaisesAnEvent()
    {
        Bot bot = BotFixture.Running();

        bot.MarkExchangeUnavailable(unavailable: true, Now).ShouldBeTrue();
        bot.MarkExchangeUnavailable(unavailable: true, Now).ShouldBeFalse();
        bot.MarkExchangeUnavailable(unavailable: false, Now).ShouldBeTrue();

        bot.RuntimeStatus.ShouldBe(BotRuntimeStatus.Running);
        bot.DomainEvents.ShouldBe([new BotExchangeUnavailable(bot.Id, Now)]);
    }

    [Test]
    public void StoppedBotHoldsItsSubmitsOnce()
    {
        Bot stopped = BotFixture.Running();
        stopped.Stop(Now);
        stopped.ClearDomainEvents();

        stopped.HoldSubmits(Now).ShouldBeTrue();
        stopped.HoldSubmits(Now).ShouldBeFalse();
        BotFixture.Running().HoldSubmits(Now).ShouldBeFalse();

        stopped.DomainEvents.ShouldBe([new BotSubmitHeld(stopped.Id, Now)]);
    }

    [Test]
    public void StartClearsHeldSubmits()
    {
        Bot bot = BotFixture.Running();
        bot.Stop(Now);
        bot.HoldSubmits(Now);

        bot.Start(BotFixture.FirstVersion, hasCheckpoint: true, tradingHalted: false, Now);

        bot.Diagnostics.ShouldBe(BotDiagnostics.None);
    }

    [Test]
    public void SnapshotRestoresTheSameBot()
    {
        Bot bot = BotFixture.FailedOnce();
        bot.RecordRun(Now);
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 25), equity: 100m, Now);
        bot.RejectStrategyVersion(StrategyVersionId.New(), Now);

        Bot restored = Bot.Restore(bot.ToSnapshot());

        restored.ToSnapshot().ShouldBe(bot.ToSnapshot());
        restored.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void RestoreRejectsAnImpossibleRestartCount()
    {
        BotSnapshot snapshot = BotFixture.Stopped().ToSnapshot() with { RestartAttempts = BotRestartSchedule.MaxCountedAttempts + 1 };

        Should.Throw<ArgumentException>(() => Bot.Restore(snapshot));
    }

    [Test]
    public void RenameNamesTheBotInAnyStateWithoutTouchingTrading()
    {
        Bot bot = BotFixture.Running();
        BotDesiredState desired = bot.DesiredState;
        BotRuntimeStatus runtime = bot.RuntimeStatus;

        bool renamed = bot.Rename(new BotName("  Main  "), Now);
        bool unchanged = bot.Rename(new BotName("Main"), Now.AddMinutes(1));

        renamed.ShouldBeTrue();
        unchanged.ShouldBeFalse();
        bot.ShouldSatisfyAllConditions(
            () => bot.Name!.Value.ShouldBe("Main"),
            () => bot.DesiredState.ShouldBe(desired),
            () => bot.RuntimeStatus.ShouldBe(runtime),
            () => bot.UpdatedAt.ShouldBe(Now),
            () => bot.DomainEvents.ShouldBeEmpty());
    }

    [Test]
    public void RenameToNullClearsTheName()
    {
        Bot bot = BotFixture.Stopped();
        bot.Rename(new BotName("Main"), Now);

        bot.Rename(name: null, Now.AddMinutes(1)).ShouldBeTrue();

        bot.Name.ShouldBeNull();
    }

    [TestCase("")]
    [TestCase("   ")]
    public void BotNameMustNotBeBlank(string value)
    {
        Should.Throw<BotRuleException>(() => new BotName(value)).Code.ShouldBe(BotRuleException.InvalidName);
    }
}