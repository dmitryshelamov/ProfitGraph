using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Metrics;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Domain.Optimization;

public sealed class WalkForwardSummaryTests
{
    private static readonly PortReference Parameter = new PortReference("period", "value");

    [Test]
    public void SummaryCombinesFreshAccountsAndUsesRatioOfWeightedMeanReturns()
    {
        WalkForwardWindowResult[] windows =
        [
            Window(index: 0, trainReturn: 10m, testReturn: 20m, pnl: 200m, trainBars: 40, testBars: 10, parameter: 2m),
            Window(index: 1, trainReturn: 30m, testReturn: -10m, pnl: -100m, trainBars: 60, testBars: 10, parameter: 6m)
        ];
        WalkForwardSummary summary = WalkForwardSummaryCalculator.Build(windows, Space());
        summary.TestTotalPnl.ShouldBe(100m);
        summary.TestCompoundedReturnPercent.ShouldBe(8m);
        summary.ProfitableWindows.ShouldBe(1);
        summary.TestWindows.ShouldBe(2);
        summary.OverallWfe.ShouldBe(1.25m);
        windows[0].Wfe.ShouldBe(8m);
        windows[1].Wfe.ShouldBe(-2m);
        summary.ParameterStability.Single().DistinctCount.ShouldBe(2);
        summary.ParameterStability.Single().RangeInSteps.ShouldBe(2m);
        summary.ParameterStability.Single().Winners.Select(winner => winner.Value).ShouldBe(new[] { 2m, 6m });
        summary.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void MissingAndFailedWindowsAreExcludedButFailedTestsRetainWinnerStability()
    {
        WalkForwardWindowResult complete = Window(index: 0);
        WalkForwardWindowResult failed = Window(index: 1, parameter: 6m) with
        {
            Status = WalkForwardWindowStatus.TestFailed, TestMetrics = null, FailureCode = "test_failure"
        };
        WalkForwardWindowResult missing = Window(index: 2) with
        {
            Status = WalkForwardWindowStatus.NoWinner, WinnerTrialIndex = null, Parameters = null, TrainMetrics = null, TestMetrics = null
        };
        WalkForwardSummary summary = WalkForwardSummaryCalculator.Build(new[] { complete, failed, missing }, Space());
        summary.TestWindows.ShouldBe(1);
        summary.TestTotalPnl.ShouldBe(100m);
        summary.TestCompoundedReturnPercent.ShouldBe(10m);
        summary.ParameterStability.Single().Winners.Count.ShouldBe(2);
        failed.Wfe.ShouldBeNull();
        missing.Wfe.ShouldBeNull();
    }

    [TestCase(0)]
    [TestCase(-10)]
    public void NonpositiveTrainReturnHasNoEfficiency(int trainReturn)
    {
        WalkForwardWindowResult window = Window(index: 0, trainReturn);
        window.Wfe.ShouldBeNull();
        WalkForwardSummaryCalculator.Build(new[] { window }, Space()).OverallWfe.ShouldBeNull();
    }

    [Test]
    public void NonpositiveTotalTrainReturnHasNoOverallEfficiencyEvenWithPositiveWindow()
    {
        WalkForwardWindowResult[] windows = [Window(index: 0, trainReturn: 10m), Window(index: 1, trainReturn: -20m)];
        windows[0].Wfe.ShouldNotBeNull();
        WalkForwardSummaryCalculator.Build(windows, Space()).OverallWfe.ShouldBeNull();
    }

    [Test]
    public void UnavailableValuesPropagateWithoutReplacingThemWithZero()
    {
        WalkForwardWindowResult window = Window(index: 0, trainReturn: null, testReturn: null, pnl: null);
        WalkForwardSummary summary = WalkForwardSummaryCalculator.Build(new[] { window }, Space());
        summary.TestTotalPnl.ShouldBeNull();
        summary.TestCompoundedReturnPercent.ShouldBeNull();
        summary.OverallWfe.ShouldBeNull();
        window.Wfe.ShouldBeNull();
        summary.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void EmptySummaryUsesEmptySumAndProductIdentities()
    {
        WalkForwardSummary summary = WalkForwardSummaryCalculator.Build(Array.Empty<WalkForwardWindowResult>(), Space());
        summary.TestTotalPnl.ShouldBe(0m);
        summary.TestCompoundedReturnPercent.ShouldBe(0m);
        summary.TestWindows.ShouldBe(0);
        summary.ProfitableWindows.ShouldBe(0);
        summary.OverallWfe.ShouldBeNull();
        summary.ParameterStability.Single().DistinctCount.ShouldBe(0);
        summary.ParameterStability.Single().RangeInSteps.ShouldBeNull();
    }

    [Test]
    public void ExactSumKeepsRepresentableFinalResultAfterLargeIntermediateTotals()
    {
        WalkForwardWindowResult[] windows =
        [
            Window(index: 0, pnl: decimal.MaxValue),
            Window(index: 1, pnl: decimal.MaxValue),
            Window(index: 2, pnl: decimal.MinValue)
        ];
        WalkForwardSummary summary = WalkForwardSummaryCalculator.Build(windows, Space());
        summary.TestTotalPnl.ShouldBe(decimal.MaxValue);
        summary.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void ExactSumDoesNotSilentlyDropSmallAddendsAtDecimalLimit()
    {
        WalkForwardWindowResult[] windows = [Window(index: 0, pnl: decimal.MaxValue), Window(index: 1, pnl: -0.1m)];
        WalkForwardSummary summary = WalkForwardSummaryCalculator.Build(windows, Space());
        summary.TestTotalPnl.ShouldBeNull();
        summary.Diagnostics.Single().Metric.ShouldBe("TestTotalPnl");
        summary.Diagnostics.Single().Code.ShouldBe(WalkForwardSummaryCalculator.UnrepresentableCode);
    }

    [Test]
    public void ArithmeticOverflowProducesNullAndStableDiagnostics()
    {
        WalkForwardWindowResult[] windows =
        [
            Window(index: 0, decimal.MaxValue, decimal.MaxValue, decimal.MaxValue),
            Window(index: 1, decimal.MaxValue, decimal.MaxValue, decimal.MaxValue)
        ];
        WalkForwardSummary summary = WalkForwardSummaryCalculator.Build(windows, Space());
        summary.TestTotalPnl.ShouldBeNull();
        summary.TestCompoundedReturnPercent.ShouldBeNull();
        summary.OverallWfe.ShouldBeNull();
        summary.Diagnostics.Select(diagnostic => diagnostic.Metric).ShouldBe(new[] { "TestTotalPnl", "TestCompoundedReturnPercent", "OverallWfe" });
        summary.Diagnostics.All(diagnostic => diagnostic.Code == WalkForwardSummaryCalculator.UnrepresentableCode).ShouldBeTrue();
    }

    [Test]
    public void CompoundingKeepsTinyRepresentableReturns()
    {
        const decimal tiny = 0.0000000000000000000000000001m;
        WalkForwardSummary single = WalkForwardSummaryCalculator.Build(new[] { Window(index: 0, tiny, tiny) }, Space());
        single.TestCompoundedReturnPercent.ShouldBe(tiny);
        WalkForwardSummary multiple = WalkForwardSummaryCalculator.Build(new[] { Window(index: 0, tiny, tiny), Window(index: 1, tiny, tiny) }, Space());
        multiple.TestCompoundedReturnPercent.ShouldBe(2m * tiny);
        multiple.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void CompoundingAvoidsOverflowInRepresentableIntermediateFactors()
    {
        WalkForwardSummary summary = WalkForwardSummaryCalculator.Build(new[] { Window(index: 0, testReturn: decimal.MaxValue) }, Space());
        summary.TestCompoundedReturnPercent.ShouldBe(decimal.MaxValue);
        summary.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void CompoundingDiagnosesNonzeroResultBelowDecimalPrecision()
    {
        const decimal tiny = 0.0000000000000000000000000001m;
        WalkForwardSummary summary = WalkForwardSummaryCalculator.Build(new[] { Window(index: 0, tiny, tiny), Window(index: 1, tiny, -tiny) }, Space());
        summary.TestCompoundedReturnPercent.ShouldBeNull();
        summary.Diagnostics.Single().Metric.ShouldBe("TestCompoundedReturnPercent");
        summary.Diagnostics.Single().Code.ShouldBe(WalkForwardSummaryCalculator.UnrepresentableCode);
    }

    [Test]
    public void TinyEqualPositiveReturnsKeepRepresentableEfficiency()
    {
        WalkForwardWindowResult window = Window(
            index: 0,
            trainReturn: 0.0000000000000000000000000001m,
            testReturn: 0.0000000000000000000000000001m,
            trainBars: 10,
            testBars: 10);
        window.Wfe.ShouldBe(1m);
        WalkForwardSummaryCalculator.Build(new[] { window }, Space()).OverallWfe.ShouldBe(1m);
        window.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void NonzeroEfficiencyBelowDecimalPrecisionIsUnavailableAndDiagnosed()
    {
        WalkForwardWindowResult window = Window(
            index: 0,
            trainReturn: 3m,
            testReturn: 0.0000000000000000000000000001m,
            trainBars: 1,
            testBars: 1);
        window.Wfe.ShouldBeNull();
        window.Diagnostics.Single().Code.ShouldBe(WalkForwardSummaryCalculator.UnrepresentableCode);
        WalkForwardSummary summary = WalkForwardSummaryCalculator.Build(new[] { window }, Space());
        summary.OverallWfe.ShouldBeNull();
        summary.Diagnostics.Select(diagnostic => diagnostic.Metric).ShouldBe(new[] { "OverallWfe", "Wfe" });
    }

    [Test]
    public void WindowEfficiencyOverflowIsDiagnosedWithWindowIndex()
    {
        WalkForwardWindowResult window = Window(index: 3, trainReturn: 0.0000000000000000000000000001m, decimal.MaxValue, trainBars: 1, testBars: 1);
        window.Wfe.ShouldBeNull();
        window.Diagnostics.Single().Code.ShouldBe(WalkForwardSummaryCalculator.UnrepresentableCode);
        window.Diagnostics.Single().WindowIndex.ShouldBe(3);
        window.Diagnostics.Single().Metric.ShouldBe("Wfe");
    }

    [Test]
    public void ParameterRangeKeepsRepresentableRatioDespiteDecimalSubtractionOverflow()
    {
        var space = new OptimizationSearchSpace(
            new[] { new OptimizationParameterRange(Parameter, decimal.MinValue, decimal.MaxValue, decimal.MaxValue, ValueType.Scalar) });
        WalkForwardWindowResult[] windows = [Window(index: 0, parameter: decimal.MinValue), Window(index: 1, parameter: decimal.MaxValue)];
        WalkForwardSummary summary = WalkForwardSummaryCalculator.Build(windows, space);
        summary.ParameterStability.Single().RangeInSteps.ShouldBe(2m);
        summary.ParameterStability.Single().DistinctCount.ShouldBe(2);
        summary.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void UnrepresentableParameterRangeIsUnavailableAndDiagnosed()
    {
        var space = new OptimizationSearchSpace(new[] { new OptimizationParameterRange(Parameter, decimal.MinValue, decimal.MaxValue, step: 1m, ValueType.Scalar) });
        WalkForwardWindowResult[] windows = [Window(index: 0, parameter: decimal.MinValue), Window(index: 1, parameter: decimal.MaxValue)];
        WalkForwardSummary summary = WalkForwardSummaryCalculator.Build(windows, space);
        summary.ParameterStability.Single().RangeInSteps.ShouldBeNull();
        summary.Diagnostics.Single().Metric.ShouldBe("ParameterStability.period.value");
        summary.Diagnostics.Single().Code.ShouldBe(WalkForwardSummaryCalculator.UnrepresentableCode);
    }

    [Test]
    public void InputOrderDoesNotChangeArithmeticOrParameterWinnerOrder()
    {
        WalkForwardWindowResult[] windows = [Window(index: 0, parameter: 2m), Window(index: 1, testReturn: -5m, parameter: 4m), Window(index: 2, parameter: 2m)];
        WalkForwardSummary forward = WalkForwardSummaryCalculator.Build(windows, Space());
        WalkForwardSummary reverse = WalkForwardSummaryCalculator.Build(windows.Reverse().ToArray(), Space());
        reverse.TestTotalPnl.ShouldBe(forward.TestTotalPnl);
        reverse.TestCompoundedReturnPercent.ShouldBe(forward.TestCompoundedReturnPercent);
        reverse.OverallWfe.ShouldBe(forward.OverallWfe);
        reverse.ParameterStability.Single().Winners.ShouldBe(forward.ParameterStability.Single().Winners);
    }

    private static OptimizationSearchSpace Space()
    {
        return new OptimizationSearchSpace(new[] { new OptimizationParameterRange(Parameter, min: 2m, max: 6m, step: 2m, ValueType.Integer) });
    }

    private static WalkForwardWindowResult Window(
        int index,
        decimal? trainReturn = 20m,
        decimal? testReturn = 10m,
        decimal? pnl = 100m,
        int trainBars = 40,
        int testBars = 10,
        decimal parameter = 2m)
    {
        var start = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
        var window = new WalkForwardWindow(index, index * testBars, trainBars, index * testBars + trainBars, testBars);

        return new WalkForwardWindowResult(
            window,
            start,
            start.AddHours(trainBars - 1),
            start.AddHours(trainBars),
            start.AddHours(trainBars + testBars - 1),
            WinnerTrialIndex: 0,
            new EngineExecutionInputs(new Dictionary<PortReference, decimal> { [Parameter] = parameter }),
            Metrics(trainReturn, pnl: 100m, trainBars),
            Metrics(testReturn, pnl, testBars),
            WalkForwardWindowStatus.Completed,
            FailureCode: null,
            Array.Empty<WalkForwardEquityPoint>());
    }

    private static SampleMetrics Metrics(decimal? returnPercent, decimal? pnl, int bars)
    {
        return new SampleMetrics(
            StartIndex: 0,
            bars,
            bars,
            BoundaryEquity: 1000m,
            EndEquity: null,
            pnl,
            RealizedPnl: null,
            UnrealizedPnl: null,
            returnPercent,
            FeesBase: 0m,
            FeesQuote: 0m,
            MaxDrawdownPercent: 1m,
            Executions: 1);
    }
}