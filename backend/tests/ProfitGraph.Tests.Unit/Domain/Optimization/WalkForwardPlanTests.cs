using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Tests.Unit.Domain.Optimization;

public sealed class WalkForwardPlanTests
{
    [Test]
    public void DefaultSettingsMatchAdoptedDefaults()
    {
        var settings = new WalkForwardSettings();
        settings.Windows.ShouldBe(6);
        settings.TrainToTestRatio.ShouldBe(4);
        settings.Scheme.ShouldBe(WalkForwardScheme.Rolling);
        settings.ShouldBe(new WalkForwardSettings(windows: 6, trainToTestRatio: 4));
    }

    [TestCase(1)]
    [TestCase(51)]
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public void InvalidWindowCountsAreRejected(int windows)
    {
        Should.Throw<WalkForwardWindowException>(() => new WalkForwardSettings(windows)).Code.ShouldBe("walk_forward_invalid_windows");
    }

    [TestCase(0)]
    [TestCase(21)]
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public void InvalidTrainToTestRatiosAreRejected(int ratio)
    {
        Should.Throw<WalkForwardWindowException>(() => new WalkForwardSettings(trainToTestRatio: ratio)).Code.ShouldBe("walk_forward_invalid_windows");
    }

    [TestCase(-1)]
    [TestCase(2)]
    public void UnknownSchemesAreRejected(int scheme)
    {
        Should.Throw<WalkForwardWindowException>(() => new WalkForwardSettings(scheme: (WalkForwardScheme)scheme)).Code.ShouldBe("walk_forward_invalid_windows");
    }

    [TestCase(arg1: 2, arg2: 1)]
    [TestCase(arg1: 50, arg2: 20)]
    public void SettingsRangeEndpointsAreValid(int windows, int ratio)
    {
        var settings = new WalkForwardSettings(windows, ratio);
        settings.Windows.ShouldBe(windows);
        settings.TrainToTestRatio.ShouldBe(ratio);
    }

    [TestCase(WalkForwardScheme.Rolling)]
    [TestCase(WalkForwardScheme.Anchored)]
    public void YearExampleUsesOnlyInSampleBarsAndEndsAtTheHoldoutBoundary(WalkForwardScheme scheme)
    {
        SampleRanges split = OutOfSampleSplit.Default.Split(8760);
        split.InSampleBars.ShouldBe(7008);
        var plan = WalkForwardPlan.Create(split.InSampleBars, new WalkForwardSettings(scheme: scheme));
        plan.Windows.Count.ShouldBe(6);

        for (var index = 0; index < plan.Windows.Count; index++)
        {
            WalkForwardWindow window = plan.Windows[index];
            window.Index.ShouldBe(index);
            window.TrainStart.ShouldBe(scheme == WalkForwardScheme.Rolling ? 8 + index * 700 : 0);
            window.TrainCount.ShouldBe(scheme == WalkForwardScheme.Rolling ? 2800 : 2808 + index * 700);
            window.TestStart.ShouldBe(2808 + index * 700);
            window.TestCount.ShouldBe(700);
        }

        (plan.Windows[^1].TestStart + plan.Windows[^1].TestCount).ShouldBe(split.InSampleBars);
    }

    [TestCase(WalkForwardScheme.Rolling)]
    [TestCase(WalkForwardScheme.Anchored)]
    public void MinimumValidSizeGivesEveryTrainAndTestOneBar(WalkForwardScheme scheme)
    {
        var plan = WalkForwardPlan.Create(inSampleBars: 3, new WalkForwardSettings(windows: 2, trainToTestRatio: 1, scheme));
        plan.Windows.ShouldBe(
            new[]
            {
                new WalkForwardWindow(Index: 0, TrainStart: 0, TrainCount: 1, TestStart: 1, TestCount: 1),
                new WalkForwardWindow(
                    Index: 1,
                    scheme == WalkForwardScheme.Rolling ? 1 : 0,
                    scheme == WalkForwardScheme.Rolling ? 1 : 2,
                    TestStart: 2,
                    TestCount: 1)
            });
    }

    [TestCase(WalkForwardScheme.Rolling)]
    [TestCase(WalkForwardScheme.Anchored)]
    public void OffsetIsDroppedOnlyByRollingWindows(WalkForwardScheme scheme)
    {
        var plan = WalkForwardPlan.Create(inSampleBars: 11, new WalkForwardSettings(windows: 2, trainToTestRatio: 2, scheme));
        plan.Windows.ShouldBe(
            new[]
            {
                new WalkForwardWindow(
                    Index: 0,
                    scheme == WalkForwardScheme.Rolling ? 3 : 0,
                    scheme == WalkForwardScheme.Rolling ? 4 : 7,
                    TestStart: 7,
                    TestCount: 2),
                new WalkForwardWindow(
                    Index: 1,
                    scheme == WalkForwardScheme.Rolling ? 5 : 0,
                    scheme == WalkForwardScheme.Rolling ? 4 : 9,
                    TestStart: 9,
                    TestCount: 2)
            });
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(9)]
    [TestCase(int.MinValue)]
    public void InsufficientRangesHaveTheStableDomainError(int inSampleBars)
    {
        foreach (WalkForwardScheme scheme in Enum.GetValues<WalkForwardScheme>())
        {
            var exception = Should.Throw<WalkForwardWindowException>(() => WalkForwardPlan.Create(inSampleBars, new WalkForwardSettings(scheme: scheme)));
            exception.Code.ShouldBe("walk_forward_invalid_windows");
            exception.InSampleBars.ShouldBe(inSampleBars);
        }
    }

    [Test]
    public void NullSettingsAreRejected()
    {
        Should.Throw<WalkForwardWindowException>(() => WalkForwardPlan.Create(inSampleBars: 10, null!)).Code.ShouldBe("walk_forward_invalid_windows");
    }

    [TestCase(WalkForwardScheme.Rolling)]
    [TestCase(WalkForwardScheme.Anchored)]
    public void TestRangesPartitionTheTailAndTrainEndsAtTestStartAcrossValidSizes(WalkForwardScheme scheme)
    {
        foreach (int windows in new[] { 2, 6, 50 })
        {
            foreach (int ratio in new[] { 1, 4, 20 })
            {
                int minimumBars = windows + ratio;

                foreach (int bars in new[] { minimumBars, minimumBars * 3 + minimumBars - 1, 7008, int.MaxValue })
                {
                    var plan = WalkForwardPlan.Create(bars, new WalkForwardSettings(windows, ratio, scheme));
                    plan.Windows.Count.ShouldBe(windows);
                    int previousEnd = plan.Windows[0].TestStart;

                    for (var index = 0; index < plan.Windows.Count; index++)
                    {
                        WalkForwardWindow window = plan.Windows[index];
                        window.Index.ShouldBe(index);
                        window.TrainStart.ShouldBeGreaterThanOrEqualTo(0);
                        window.TrainCount.ShouldBeGreaterThanOrEqualTo(1);
                        window.TestCount.ShouldBeGreaterThanOrEqualTo(1);
                        window.TestStart.ShouldBe(checked(window.TrainStart + window.TrainCount));
                        window.TestStart.ShouldBe(previousEnd);
                        previousEnd = checked(window.TestStart + window.TestCount);
                    }

                    previousEnd.ShouldBe(bars);
                    plan.Windows.ShouldBe(WalkForwardPlan.Create(bars, new WalkForwardSettings(windows, ratio, scheme)).Windows);
                }
            }
        }
    }
}