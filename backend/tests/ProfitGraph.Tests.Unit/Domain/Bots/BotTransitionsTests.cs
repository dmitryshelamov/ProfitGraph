using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Domain.Bots;

/// <summary>
///     Owner controls and runtime reports of a bot. Rules follow Profit Fox Designer ADRs 0054–0057, where they lived in SQL.
/// </summary>
public sealed class BotTransitionsTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created.AddMinutes(5);

    [Test]
    public void CreatedBotIsStoppedAndHasNeverRun()
    {
        Bot bot = BotFixture.Stopped();

        bot.ShouldSatisfyAllConditions(
            () => bot.DesiredState.ShouldBe(BotDesiredState.Stopped),
            () => bot.RuntimeStatus.ShouldBe(BotRuntimeStatus.Created),
            () => bot.HasRun.ShouldBeFalse(),
            () => bot.StatusReason.ShouldBeNull(),
            () => bot.DomainEvents.ShouldBeEmpty());
    }

    [Test]
    public void AllocationCapMustBePositive()
    {
        Bot bot = BotFixture.Stopped();

        Should.Throw<BotRuleException>(() => bot.ChangeParameters(bot.Parameters, allocationCap: 0m, Now)).Code.ShouldBe(BotRuleException.InvalidSettings);
    }

    [Test]
    public void FirstStartRunsTheLatestVersion()
    {
        Bot bot = BotFixture.Stopped();
        var latest = StrategyVersionId.New();

        bot.Start(latest, hasCheckpoint: false, tradingHalted: false, Now).ShouldBeTrue();

        bot.ShouldSatisfyAllConditions(
            () => bot.DesiredState.ShouldBe(BotDesiredState.Running),
            () => bot.RuntimeStatus.ShouldBe(BotRuntimeStatus.Created),
            () => bot.StrategyVersionId.ShouldBe(latest),
            () => bot.FirstStartedAt.ShouldBe(Now),
            () => bot.DomainEvents.ShouldBe([new BotStarted(bot.Id, latest, Now)]));
    }

    [Test]
    public void StartWithACheckpointKeepsTheStoredVersion()
    {
        Bot bot = BotFixture.Stopped();

        bot.Start(StrategyVersionId.New(), hasCheckpoint: true, tradingHalted: false, Now);

        bot.StrategyVersionId.ShouldBe(BotFixture.FirstVersion);
    }

    [Test]
    public void StartIsRefusedWhileTradingIsHalted()
    {
        Bot bot = BotFixture.Stopped();

        Should.Throw<BotRuleException>(() => bot.Start(BotFixture.FirstVersion, hasCheckpoint: false, tradingHalted: true, Now))
            .Code.ShouldBe(BotRuleException.TradingHalted);
        bot.DesiredState.ShouldBe(BotDesiredState.Stopped);
    }

    [Test]
    public void StartingARunningBotChangesNothing()
    {
        Bot bot = BotFixture.Running();

        bot.Start(StrategyVersionId.New(), hasCheckpoint: false, tradingHalted: false, Now).ShouldBeFalse();

        bot.RuntimeStatus.ShouldBe(BotRuntimeStatus.Running);
        bot.DomainEvents.ShouldBeEmpty();
    }

    [TestCase(BotStatusReason.IntentFailed)]
    [TestCase(BotStatusReason.InsufficientBalance)]
    public void StartReArmsABotDegradedByAFailedIntentOrBalance(BotStatusReason reason)
    {
        Bot bot = BotFixture.DegradedBy(reason);

        bot.Start(BotFixture.FirstVersion, hasCheckpoint: true, tradingHalted: false, Now).ShouldBeTrue();

        bot.ShouldSatisfyAllConditions(
            () => bot.RuntimeStatus.ShouldBe(BotRuntimeStatus.Created),
            () => bot.StatusReason.ShouldBeNull(),
            () => bot.RestartAttempts.ShouldBe(0));
    }

    [TestCase(BotStatusReason.ForeignOrder)]
    [TestCase(BotStatusReason.UnresolvedOrder)]
    [TestCase(BotStatusReason.KeyPermissions)]
    public void StartDoesNotClearOtherSafetyReasonsWhileTheBotShouldRun(BotStatusReason reason)
    {
        Bot bot = BotFixture.DegradedBy(reason);

        bot.Start(BotFixture.FirstVersion, hasCheckpoint: true, tradingHalted: false, Now).ShouldBeFalse();

        bot.StatusReason.ShouldBe(reason);
    }

    [Test]
    public void StartClearsAKillAndTheDailyBaseline()
    {
        Bot bot = BotFixture.Running();
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 25), equity: 100m, Now);
        bot.Kill(BotStatusReason.Killed, Now);

        bot.Start(BotFixture.FirstVersion, hasCheckpoint: true, tradingHalted: false, Now.AddMinutes(1));

        bot.ShouldSatisfyAllConditions(
            () => bot.StatusReason.ShouldBeNull(),
            () => bot.DayBaseline.ShouldBeNull(),
            () => bot.Diagnostics.ShouldBe(BotDiagnostics.None));
    }

    [Test]
    public void FirstStartTimeIsKeptAcrossStarts()
    {
        Bot bot = BotFixture.Started();
        bot.Stop(Now);

        bot.Start(BotFixture.FirstVersion, hasCheckpoint: false, tradingHalted: false, Now.AddHours(1));

        bot.FirstStartedAt.ShouldBe(BotFixture.Created);
    }

    [Test]
    public void StopChangesOnlyTheDesiredState()
    {
        Bot bot = BotFixture.Running();

        bot.Stop(Now).ShouldBeTrue();
        bot.Stop(Now).ShouldBeFalse();

        bot.ShouldSatisfyAllConditions(
            () => bot.DesiredState.ShouldBe(BotDesiredState.Stopped),
            () => bot.RuntimeStatus.ShouldBe(BotRuntimeStatus.Running),
            () => bot.DomainEvents.ShouldBe([new BotStopped(bot.Id, Now)]));
    }

    [TestCase(BotStatusReason.Killed)]
    [TestCase(BotStatusReason.DailyLossLimit)]
    public void KillStopsTheBotAtOnce(BotStatusReason reason)
    {
        Bot bot = BotFixture.FailedOnce();
        bot.MarkFeedStale(stale: true, Now);

        bot.Kill(reason, Now);

        bot.ShouldSatisfyAllConditions(
            () => bot.DesiredState.ShouldBe(BotDesiredState.Stopped),
            () => bot.RuntimeStatus.ShouldBe(BotRuntimeStatus.Stopped),
            () => bot.StatusReason.ShouldBe(reason),
            () => bot.Diagnostics.ShouldBe(BotDiagnostics.None),
            () => bot.NextRestartAt.ShouldBeNull(),
            () => bot.DomainEvents.ShouldContain(new BotKilled(bot.Id, reason, Now)));
    }

    [Test]
    public void KillNeedsAControlReason()
    {
        Should.Throw<ArgumentException>(() => BotFixture.Running().Kill(BotStatusReason.ForeignOrder, Now));
    }

    [Test]
    public void RunningBotCanBeLeased()
    {
        BotFixture.Started().CanAcquireLease(Now).ShouldBeTrue();
    }

    [Test]
    public void StoppedBotCannotBeLeased()
    {
        Bot bot = BotFixture.Stopped();

        bot.CanAcquireLease(Now).ShouldBeFalse();
        Should.Throw<BotRuleException>(() => bot.RecordLeaseAcquired(Now)).Code.ShouldBe(BotRuleException.NotLeasable);
    }

    [Test]
    public void FailedBotIsLeasedAgainOnlyAfterItsRestartTime()
    {
        Bot bot = BotFixture.FailedOnce();

        bot.NextRestartAt.ShouldBe(BotFixture.Created.AddSeconds(30));
        bot.CanAcquireLease(BotFixture.Created.AddSeconds(29)).ShouldBeFalse();
        bot.CanAcquireLease(BotFixture.Created.AddSeconds(30)).ShouldBeTrue();
    }

    [Test]
    public void LeasingADegradedBotCountsARestart()
    {
        Bot bot = BotFixture.FailedOnce();

        bot.RecordLeaseAcquired(BotFixture.Created.AddSeconds(30));

        bot.RestartAttempts.ShouldBe(1);
    }

    [Test]
    public void LeasingAHealthyBotCountsNoRestart()
    {
        Bot bot = BotFixture.Started();

        bot.RecordLeaseAcquired(Now);

        bot.RestartAttempts.ShouldBe(0);
    }

    [Test]
    public void RestartsWaitThirtySecondsThenEveryMinuteWithoutALimit()
    {
        Bot bot = BotFixture.Running();
        DateTimeOffset now = BotFixture.Created;
        var delays = new List<TimeSpan>();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            bot.RecordRuntimeFailure(now);
            delays.Add(bot.NextRestartAt!.Value - now);
            now = bot.NextRestartAt.Value;
            bot.RecordLeaseAcquired(now);
            bot.MarkStarting(now);
        }

        bot.RecordRuntimeFailure(now);

        delays.ShouldBe([TimeSpan.FromSeconds(30), ..Enumerable.Repeat(TimeSpan.FromMinutes(1), count: 19)]);
        bot.ShouldSatisfyAllConditions(
            () => bot.RestartAttempts.ShouldBe(BotRestartSchedule.MaxCountedAttempts),
            () => bot.NextRestartAt.ShouldBe(now.AddMinutes(1)),
            () => bot.CanAcquireLease(now.AddMinutes(1)).ShouldBeTrue());
    }

    [Test]
    public void ARuntimeFailureLeftWithoutARestartTimeIsDueAtOnce()
    {
        BotSnapshot spent = BotFixture.FailedOnce().ToSnapshot() with
        {
            RestartAttempts = BotRestartSchedule.MaxCountedAttempts,
            NextRestartAt = null
        };

        Bot bot = Bot.Restore(spent);
        bot.RecordLeaseAcquired(Now);

        bot.RestartAttempts.ShouldBe(BotRestartSchedule.MaxCountedAttempts);
    }

    [Test]
    public void FilledOrderIsRecordedAsAFact()
    {
        Bot bot = BotFixture.Running();
        var fill = new BotOrderFill(
            engineOrderId: 3,
            OrderSide.Sell,
            "exit",
            grossBase: 0.002m,
            grossQuote: 130m,
            fee: 0.13m,
            FeeAsset.Quote,
            realizedPnl: 4.5m,
            quantityStep: 0.000001m,
            priceTick: 0.01m,
            quotePrecision: 8);

        bot.RecordOrderFilled(fill, Now);

        bot.DomainEvents.ShouldBe([new BotOrderFilled(bot.Id, fill, Now)]);
        bot.UpdatedAt.ShouldBe(Now);
        fill.AveragePrice.ShouldBe(65000m);
    }

    [Test]
    public void FilledOrderHasARealizedProfitExactlyWhenItSold()
    {
        Should.Throw<ArgumentException>(() => new BotOrderFill(
            engineOrderId: 1,
            OrderSide.Buy,
            tag: null,
            grossBase: 1m,
            grossQuote: 1m,
            fee: 0m,
            FeeAsset.Base,
            realizedPnl: 0m,
            quantityStep: 1m,
            priceTick: 1m,
            quotePrecision: 2));
        Should.Throw<ArgumentException>(() => new BotOrderFill(
            engineOrderId: 1,
            OrderSide.Sell,
            tag: null,
            grossBase: 1m,
            grossQuote: 1m,
            fee: 0m,
            FeeAsset.Quote,
            realizedPnl: null,
            quantityStep: 1m,
            priceTick: 1m,
            quotePrecision: 2));
    }

    [Test]
    public void LastFailedRestartReportsThatTheBotWaitsForItsOwnerOnce()
    {
        Bot bot = BotFixture.Running();
        DateTimeOffset now = BotFixture.Created;

        for (var attempt = 0; attempt < BotRestartSchedule.MaxAttempts; attempt++)
        {
            bot.RecordRuntimeFailure(now);
            now = bot.NextRestartAt!.Value;
            bot.RecordLeaseAcquired(now);
            bot.MarkStarting(now);
        }

        bot.ShouldSatisfyAllConditions(() => bot.DomainEvents.OfType<BotRestartsExhausted>().ShouldBeEmpty());

        bot.RecordRuntimeFailure(now);
        bot.RecordRuntimeFailure(now.AddMinutes(1));

        bot.DomainEvents.OfType<BotRestartsExhausted>().ShouldBe([new BotRestartsExhausted(bot.Id, now)]);
    }

    [Test]
    public void FilledOrderIsRecordedAsAFact()
    {
        Bot bot = BotFixture.Running();
        var fill = new BotOrderFill(
            engineOrderId: 3,
            OrderSide.Sell,
            "exit",
            grossBase: 0.002m,
            grossQuote: 130m,
            fee: 0.13m,
            FeeAsset.Quote,
            realizedPnl: 4.5m,
            quantityStep: 0.000001m,
            priceTick: 0.01m,
            quotePrecision: 8);

        bot.RecordOrderFilled(fill, Now);

        bot.DomainEvents.ShouldBe([new BotOrderFilled(bot.Id, fill, Now)]);
        bot.UpdatedAt.ShouldBe(Now);
        fill.AveragePrice.ShouldBe(65000m);
    }

    [Test]
    public void FilledOrderHasARealizedProfitExactlyWhenItSold()
    {
        Should.Throw<ArgumentException>(() => new BotOrderFill(
            engineOrderId: 1,
            OrderSide.Buy,
            tag: null,
            grossBase: 1m,
            grossQuote: 1m,
            fee: 0m,
            FeeAsset.Base,
            realizedPnl: 0m,
            quantityStep: 1m,
            priceTick: 1m,
            quotePrecision: 2));
        Should.Throw<ArgumentException>(() => new BotOrderFill(
            engineOrderId: 1,
            OrderSide.Sell,
            tag: null,
            grossBase: 1m,
            grossQuote: 1m,
            fee: 0m,
            FeeAsset.Quote,
            realizedPnl: null,
            quantityStep: 1m,
            priceTick: 1m,
            quotePrecision: 2));
    }

    [Test]
    public void RuntimeFailureDegradesWithOneEvent()
    {
        Bot bot = BotFixture.Running();

        bot.RecordRuntimeFailure(Now).ShouldBeTrue();
        bot.RecordRuntimeFailure(Now).ShouldBeTrue();

        bot.ShouldSatisfyAllConditions(
            () => bot.RuntimeStatus.ShouldBe(BotRuntimeStatus.Degraded),
            () => bot.StatusReason.ShouldBe(BotStatusReason.RuntimeFailure),
            () => bot.DomainEvents.ShouldBe([new BotDegraded(bot.Id, BotStatusReason.RuntimeFailure, Now)]));
    }

    [Test]
    public void RuntimeFailureCarriesItsDetailOnTheEventAsOneLimitedLine()
    {
        Bot bot = BotFixture.Running();

        bot.RecordRuntimeFailure(Now, "  The strategy\n version   does not exist.  ");

        bot.DomainEvents.ShouldBe([new BotDegraded(bot.Id, BotStatusReason.RuntimeFailure, Now, "The strategy version does not exist.")]);
    }

    [Test]
    public void ADetailLongerThanTheLimitIsCut()
    {
        Bot bot = BotFixture.Running();

        bot.RecordRuntimeFailure(Now, new string(c: 'x', count: 1000));

        ((BotDegraded)bot.DomainEvents.Single()).Detail!.Length.ShouldBe(expected: 300);
    }

    [Test]
    public void ASafetyDegradationCarriesItsDetail()
    {
        Bot bot = BotFixture.Running();

        bot.Degrade(BotStatusReason.HistoryDataCorrection, Now, "The venue changed a closed candle.");

        bot.DomainEvents.ShouldBe([new BotDegraded(bot.Id, BotStatusReason.HistoryDataCorrection, Now, "The venue changed a closed candle.")]);
    }

    [Test]
    public void ARestatedCandleIsRecordedAsAnEvent()
    {
        Bot bot = BotFixture.Running();

        bot.RecordHistoryRestated(Now.AddHours(-1), previousClose: 100m, restatedClose: 101m, Now);

        bot.DomainEvents.ShouldBe([new BotHistoryRestated(bot.Id, Now.AddHours(-1), PreviousClose: 100m, RestatedClose: 101m, Now)]);
    }

    [Test]
    public void RuntimeFailureDoesNotReplaceASafetyReason()
    {
        Bot bot = BotFixture.DegradedBy(BotStatusReason.ForeignOrder);

        bot.RecordRuntimeFailure(Now).ShouldBeFalse();

        bot.StatusReason.ShouldBe(BotStatusReason.ForeignOrder);
        bot.NextRestartAt.ShouldBeNull();
    }

    [Test]
    public void SafetyReasonWaitsForTheOwner()
    {
        Bot bot = BotFixture.FailedOnce();

        bot.Degrade(BotStatusReason.UnresolvedOrder, Now).ShouldBeTrue();

        bot.ShouldSatisfyAllConditions(
            () => bot.StatusReason.ShouldBe(BotStatusReason.UnresolvedOrder),
            () => bot.NextRestartAt.ShouldBeNull(),
            () => bot.CanAcquireLease(Now.AddDays(1)).ShouldBeFalse(),
            () => bot.DomainEvents.ShouldBe([new BotDegraded(bot.Id, BotStatusReason.UnresolvedOrder, Now)]));
    }

    [Test]
    public void TheSameSafetyReasonIsRecordedOnce()
    {
        Bot bot = BotFixture.DegradedBy(BotStatusReason.InsufficientBalance);

        bot.Degrade(BotStatusReason.InsufficientBalance, Now).ShouldBeFalse();
        bot.Degrade(BotStatusReason.ForeignOrder, Now).ShouldBeTrue();

        bot.DomainEvents.ShouldBe([new BotDegraded(bot.Id, BotStatusReason.ForeignOrder, Now)]);
    }

    [Test]
    public void StoppedBotIsNotDegraded()
    {
        Bot bot = BotFixture.Stopped();

        bot.Degrade(BotStatusReason.ForeignOrder, Now).ShouldBeFalse();

        bot.RuntimeStatus.ShouldBe(BotRuntimeStatus.Created);
    }

    [TestCase(BotStatusReason.RuntimeFailure)]
    [TestCase(BotStatusReason.Killed)]
    public void DegradeTakesOnlySafetyReasons(BotStatusReason reason)
    {
        Should.Throw<ArgumentException>(() => BotFixture.Running().Degrade(reason, Now));
    }

    [Test]
    public void StartingRequiresABotThatShouldRun()
    {
        Bot stopped = BotFixture.Stopped();
        Bot started = BotFixture.Started();

        stopped.MarkStarting(Now).ShouldBeFalse();
        started.MarkStarting(Now).ShouldBeTrue();

        started.RuntimeStatus.ShouldBe(BotRuntimeStatus.Starting);
        started.DomainEvents.ShouldBe([new BotRuntimeStarting(started.Id, Now)]);
    }

    [Test]
    public void DegradedBotCannotReportRunningDirectly()
    {
        Bot bot = BotFixture.FailedOnce();

        bot.MarkRunning(Now).ShouldBeFalse();

        bot.RuntimeStatus.ShouldBe(BotRuntimeStatus.Degraded);
    }

    [Test]
    public void RunningAfterARestartIsARecovery()
    {
        Bot bot = BotFixture.FailedOnce();
        bot.RecordLeaseAcquired(BotFixture.Created.AddSeconds(30));
        bot.MarkStarting(Now);
        bot.ClearDomainEvents();

        bot.MarkRunning(Now).ShouldBeTrue();

        bot.RuntimeStatus.ShouldBe(BotRuntimeStatus.Running);
        bot.DomainEvents.ShouldBe([new BotRecovered(bot.Id, Now)]);
    }

    [Test]
    public void FirstRunningIsNotARecovery()
    {
        Bot bot = BotFixture.Started();
        bot.MarkStarting(Now);
        bot.ClearDomainEvents();

        bot.MarkRunning(Now).ShouldBeTrue();

        bot.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void StoppedRuntimeKeepsAKillReasonOnly()
    {
        Bot killed = BotFixture.Running();
        killed.Kill(BotStatusReason.DailyLossLimit, Now);
        Bot failed = BotFixture.FailedOnce();
        failed.Stop(Now);

        killed.MarkRuntimeStopped(Now).ShouldBeFalse();
        failed.MarkRuntimeStopped(Now).ShouldBeTrue();

        killed.StatusReason.ShouldBe(BotStatusReason.DailyLossLimit);
        failed.StatusReason.ShouldBeNull();
        failed.DomainEvents.ShouldContain(new BotRuntimeStopped(failed.Id, Now));
    }

    [Test]
    public void LiveCandleRootResetsTheRestartStreak()
    {
        Bot bot = BotFixture.FailedOnce();
        bot.RecordLeaseAcquired(BotFixture.Created.AddSeconds(30));

        bot.ResetRestartStreak(Now);

        bot.RestartAttempts.ShouldBe(0);
        bot.NextRestartAt.ShouldBeNull();
    }

    [Test]
    public void FirstRootAfterAStartSetsTheBaseline()
    {
        Bot bot = BotFixture.Running();
        var day = new DateOnly(year: 2026, month: 10, day: 25);

        bot.RecordDayEquity(day, equity: 100m, Now).ShouldBeFalse();
        bot.RecordDayEquity(day, equity: 95m, Now).ShouldBeFalse();

        bot.DayBaseline.ShouldBe(new DailyLossBaseline(day, Equity: 100m, LastEquity: 95m));
    }

    [Test]
    public void LossReachingTheDailyLimitKillsTheBot()
    {
        Bot bot = BotFixture.Running();
        var day = new DateOnly(year: 2026, month: 10, day: 25);
        bot.RecordDayEquity(day, equity: 100m, Now);

        bot.RecordDayEquity(day, equity: 90.01m, Now).ShouldBeFalse();
        bot.RecordDayEquity(day, equity: 90m, Now).ShouldBeTrue();

        bot.StatusReason.ShouldBe(BotStatusReason.DailyLossLimit);
        bot.DesiredState.ShouldBe(BotDesiredState.Stopped);
    }

    [Test]
    public void ANewUtcDayTakesThePreviousDaysLastEquityAsItsBaseline()
    {
        Bot bot = BotFixture.Running();
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 25), equity: 100m, Now);
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 25), equity: 104m, Now);

        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 26), equity: 97m, Now).ShouldBeFalse();

        bot.DayBaseline.ShouldBe(new DailyLossBaseline(new DateOnly(year: 2026, month: 10, day: 26), Equity: 104m, LastEquity: 97m));
        bot.DesiredState.ShouldBe(BotDesiredState.Running);
    }

    [Test]
    public void DailyBotLosingTheLimitBetweenTwoDailyRootsIsKilled()
    {
        Bot bot = BotFixture.Running();
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 25), equity: 100m, Now).ShouldBeFalse();

        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 26), equity: 89m, Now).ShouldBeTrue();

        bot.StatusReason.ShouldBe(BotStatusReason.DailyLossLimit);
        bot.DesiredState.ShouldBe(BotDesiredState.Stopped);
    }

    [Test]
    public void LossAcrossMidnightIsCounted()
    {
        Bot bot = BotFixture.Running();
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 25), equity: 100m, Now);
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 25), equity: 96m, Now);

        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 26), equity: 91m, Now).ShouldBeFalse();
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 26), equity: 86m, Now).ShouldBeTrue();

        bot.StatusReason.ShouldBe(BotStatusReason.DailyLossLimit);
    }

    [Test]
    public void LossBelowTheLimitAcrossDaysDoesNotKill()
    {
        Bot bot = BotFixture.Running();
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 25), equity: 100m, Now);

        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 26), equity: 92m, Now).ShouldBeFalse();
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 27), equity: 84m, Now).ShouldBeFalse();

        bot.ShouldSatisfyAllConditions(
            () => bot.DesiredState.ShouldBe(BotDesiredState.Running),
            () => bot.StatusReason.ShouldBeNull(),
            () => bot.DayBaseline.ShouldBe(new DailyLossBaseline(new DateOnly(year: 2026, month: 10, day: 27), Equity: 92m, LastEquity: 84m)));
    }

    [Test]
    public void StartAfterADailyLossKillMeasuresFromItsFirstRoot()
    {
        Bot bot = BotFixture.Running();
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 25), equity: 100m, Now);
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 25), equity: 90m, Now).ShouldBeTrue();
        bot.Start(BotFixture.FirstVersion, hasCheckpoint: true, tradingHalted: false, Now.AddMinutes(1));

        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 26), equity: 85m, Now.AddMinutes(2)).ShouldBeFalse();

        bot.DayBaseline.ShouldBe(new DailyLossBaseline(new DateOnly(year: 2026, month: 10, day: 26), Equity: 85m, LastEquity: 85m));
    }

    [Test]
    public void StoppedBotRecordsNoDailyEquity()
    {
        Bot bot = BotFixture.Stopped();

        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 25), equity: 100m, Now).ShouldBeFalse();

        bot.DayBaseline.ShouldBeNull();
    }

    [Test]
    public void FirstCheckpointMarksTheRun()
    {
        Bot bot = BotFixture.Running();

        bot.RecordRun(Now);
        bot.RecordRun(Now.AddMinutes(1));

        bot.HasRun.ShouldBeTrue();
    }

    [Test]
    public void UntradedRunIsForgottenOnlyWhenStopped()
    {
        Bot running = BotFixture.Running();
        running.RecordRun(Now);
        Bot stopped = BotFixture.Running();
        stopped.RecordRun(Now);
        stopped.Stop(Now);
        stopped.MarkRuntimeStopped(Now);
        stopped.ClearDomainEvents();

        Should.Throw<BotRuleException>(() => running.ForgetUntradedRun(Now)).Code.ShouldBe(BotRuleException.MustBeStopped);
        stopped.ForgetUntradedRun(Now);

        stopped.ShouldSatisfyAllConditions(
            () => stopped.HasRun.ShouldBeFalse(),
            () => stopped.FirstStartedAt.ShouldBeNull(),
            () => stopped.DomainEvents.ShouldBe([new BotRunRestarted(stopped.Id, Now)]));
    }

    [Test]
    public void ManualOrdersNeedARunningBot()
    {
        Should.NotThrow(() => BotFixture.Running().EnsureAcceptsManualOrders());
        Should.Throw<BotRuleException>(() => BotFixture.Started().EnsureAcceptsManualOrders()).Code.ShouldBe(BotRuleException.NotRunning);
        Should.Throw<BotRuleException>(() => BotFixture.FailedOnce().EnsureAcceptsManualOrders()).Code.ShouldBe(BotRuleException.NotRunning);
    }

    [Test]
    public void OrderCancelsNeedARunningBot()
    {
        Should.NotThrow(() => BotFixture.Running().EnsureAcceptsOrderCancels());
        Should.Throw<BotRuleException>(() => BotFixture.Started().EnsureAcceptsOrderCancels()).Code.ShouldBe(BotRuleException.NotRunning);
        Should.Throw<BotRuleException>(() => BotFixture.FailedOnce().EnsureAcceptsOrderCancels()).Code.ShouldBe(BotRuleException.NotRunning);
        Should.Throw<BotRuleException>(() => BotFixture.Stopped().EnsureAcceptsOrderCancels()).Code.ShouldBe(BotRuleException.NotRunning);
    }

    [Test]
    public void SubmitsNeedARunningHealthyBotAndNoHalt()
    {
        BotFixture.Running().CanSubmit(tradingHalted: false).ShouldBeTrue();
        BotFixture.Running().CanSubmit(tradingHalted: true).ShouldBeFalse();
        BotFixture.FailedOnce().CanSubmit(tradingHalted: false).ShouldBeFalse();
        BotFixture.Stopped().CanSubmit(tradingHalted: false).ShouldBeFalse();
    }
}