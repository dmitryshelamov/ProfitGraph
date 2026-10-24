using System.Numerics;

using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Domain.Optimization;

public static class WalkForwardSummaryCalculator
{
    public const string UnrepresentableCode = "walk_forward_metric_unrepresentable";

    public static WalkForwardSummary Build(IReadOnlyList<WalkForwardWindowResult> windows, OptimizationSearchSpace searchSpace)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(searchSpace);
        WalkForwardWindowResult[] ordered = windows.OrderBy(result => result.Window.Index)
            .ToArray();
        WalkForwardWindowResult[] tests = ordered.Where(result => result.Status == WalkForwardWindowStatus.Completed && result.TestMetrics is not null)
            .ToArray();
        var diagnostics = new List<WalkForwardMetricDiagnostic>();
        decimal? pnl = Calculate("TestTotalPnl", () => Sum(tests.Select(result => result.TestMetrics!.TotalPnl)), diagnostics);
        decimal? compounded = Calculate("TestCompoundedReturnPercent", () => Compound(tests), diagnostics);
        decimal? overall = Calculate("OverallWfe", () => OverallEfficiency(tests), diagnostics);
        var stability = new List<WalkForwardParameterStability>();

        foreach (OptimizationParameterRange parameter in searchSpace.Parameters)
        {
            WalkForwardParameterWinner[] winners = ordered.Where(result => result.WinnerTrialIndex is not null && result.Parameters is not null)
                .Select(result => new WalkForwardParameterWinner(result.Window.Index, result.Parameters!.OptimizedValues[parameter.Reference]))
                .ToArray();
            decimal? range = winners.Length == 0
                ? null
                : Calculate(
                    $"ParameterStability.{parameter.Reference.NodeId}.{parameter.Reference.PortKey}",
                    () => RangeInSteps(winners, parameter.Step),
                    diagnostics);
            stability.Add(
                new WalkForwardParameterStability(parameter.Reference, Array.AsReadOnly(winners), winners.Select(winner => winner.Value).Distinct().Count(), range));
        }

        foreach (WalkForwardWindowResult result in ordered)
        {
            diagnostics.AddRange(WindowEfficiency(result).Diagnostics);
        }

        return new WalkForwardSummary(
            pnl,
            compounded,
            tests.Count(result => result.TestMetrics!.TotalPnl > 0m),
            tests.Length,
            overall,
            stability.AsReadOnly(),
            diagnostics.AsReadOnly());
    }

    public static WalkForwardEfficiency WindowEfficiency(WalkForwardWindowResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var diagnostics = new List<WalkForwardMetricDiagnostic>();
        decimal? value = result.Status == WalkForwardWindowStatus.Completed
            ? Calculate(
                "Wfe",
                () => Efficiency(
                    result.TestMetrics?.ReturnPercent,
                    result.Window.TestCount,
                    result.TrainMetrics?.ReturnPercent,
                    result.Window.TrainCount),
                diagnostics,
                result.Window.Index)
            : null;

        return new WalkForwardEfficiency(value, diagnostics.AsReadOnly());
    }

    private static decimal RangeInSteps(IReadOnlyList<WalkForwardParameterWinner> winners, decimal step)
    {
        decimal minimum = winners.Min(winner => winner.Value);
        decimal maximum = winners.Max(winner => winner.Value);
        int scale = Math.Max(ExactDecimalGrid.Scale(minimum), Math.Max(ExactDecimalGrid.Scale(maximum), ExactDecimalGrid.Scale(step)));
        BigInteger spread = ExactDecimalGrid.ToScaledInteger(maximum, scale) - ExactDecimalGrid.ToScaledInteger(minimum, scale);

        return RatioToDecimal(spread, ExactDecimalGrid.ToScaledInteger(step, scale));
    }

    private static decimal? OverallEfficiency(IReadOnlyList<WalkForwardWindowResult> tests)
    {
        decimal? train = Sum(tests.Select(result => result.TrainMetrics?.ReturnPercent));
        decimal? test = Sum(tests.Select(result => result.TestMetrics!.ReturnPercent));
        long trainBars = tests.Aggregate(seed: 0L, (total, result) => checked(total + result.Window.TrainCount));
        long testBars = tests.Aggregate(seed: 0L, (total, result) => checked(total + result.Window.TestCount));

        return Efficiency(test, testBars, train, trainBars);
    }

    private static decimal? Efficiency(decimal? testReturn, long testBars, decimal? trainReturn, long trainBars)
    {
        if (testReturn is null || trainReturn is null || trainReturn <= 0m || testBars <= 0 || trainBars <= 0)
        {
            return null;
        }

        int scale = Math.Max(ExactDecimalGrid.Scale(testReturn.Value), ExactDecimalGrid.Scale(trainReturn.Value));
        BigInteger numerator = ExactDecimalGrid.ToScaledInteger(testReturn.Value, scale) * trainBars;
        BigInteger denominator = ExactDecimalGrid.ToScaledInteger(trainReturn.Value, scale) * testBars;

        return RatioToDecimal(numerator, denominator);
    }

    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        decimal?[] terms = values.ToArray();

        if (terms.Any(value => value is null))
        {
            return null;
        }

        int scale = terms.Length == 0 ? 0 : terms.Max(value => ExactDecimalGrid.Scale(value!.Value));
        BigInteger coefficient = terms.Aggregate(
            BigInteger.Zero,
            (sum, value) => sum + ExactDecimalGrid.ToScaledInteger(value!.Value, scale));
        bool negative = coefficient.Sign < 0;
        coefficient = BigInteger.Abs(coefficient);

        while (scale > 0 && coefficient % 10 == BigInteger.Zero)
        {
            coefficient /= 10;
            scale--;
        }

        if (coefficient > (BigInteger.One << 96) - BigInteger.One)
        {
            throw new OverflowException("The exact sum is not representable as decimal.");
        }

        return new decimal(
            unchecked((int)(uint)(coefficient & uint.MaxValue)),
            unchecked((int)(uint)((coefficient >> 32) & uint.MaxValue)),
            unchecked((int)(uint)((coefficient >> 64) & uint.MaxValue)),
            negative,
            (byte)scale);
    }

    private static decimal? Compound(IEnumerable<WalkForwardWindowResult> tests)
    {
        BigInteger numerator = BigInteger.One;
        BigInteger denominator = BigInteger.One;

        foreach (WalkForwardWindowResult result in tests)
        {
            if (result.TestMetrics!.ReturnPercent is not decimal value)
            {
                return null;
            }

            int scale = ExactDecimalGrid.Scale(value);
            BigInteger divisor = 100 * BigInteger.Pow(value: 10, scale);
            numerator *= divisor + ExactDecimalGrid.ToScaledInteger(value, scale);
            denominator *= divisor;
            BigInteger common = BigInteger.GreatestCommonDivisor(numerator, denominator);
            numerator /= common;
            denominator /= common;
        }

        // Keep factors exact until the final return, so adding a tiny return to one cannot erase it.
        return RatioToDecimal((numerator - denominator) * 100, denominator);
    }

    private static decimal RatioToDecimal(BigInteger numerator, BigInteger denominator)
    {
        bool negative = numerator.Sign < 0;
        numerator = BigInteger.Abs(numerator);
        BigInteger maximum = (BigInteger.One << 96) - BigInteger.One;

        if (numerator > maximum * denominator)
        {
            throw new OverflowException("The derived metric is outside decimal's range.");
        }

        for (var scale = 28; scale >= 0; scale--)
        {
            BigInteger coefficient = BigInteger.DivRem(numerator * BigInteger.Pow(value: 10, scale), denominator, out BigInteger remainder);
            int comparison = (remainder * 2).CompareTo(denominator);

            if (comparison > 0 || (comparison == 0 && !coefficient.IsEven))
            {
                coefficient += BigInteger.One;
            }

            if (coefficient > maximum)
            {
                continue;
            }

            if (coefficient.IsZero && !numerator.IsZero)
            {
                throw new OverflowException("The nonzero derived metric is below decimal's precision.");
            }

            return new decimal(
                unchecked((int)(uint)(coefficient & uint.MaxValue)),
                unchecked((int)(uint)((coefficient >> 32) & uint.MaxValue)),
                unchecked((int)(uint)((coefficient >> 64) & uint.MaxValue)),
                negative,
                (byte)scale);
        }

        throw new OverflowException("The derived metric is not representable as decimal.");
    }

    private static decimal? Calculate(
        string metric,
        Func<decimal?> calculate,
        ICollection<WalkForwardMetricDiagnostic> diagnostics,
        int? windowIndex = null)
    {
        try
        {
            return calculate();
        }
        catch (OverflowException)
        {
            diagnostics.Add(new WalkForwardMetricDiagnostic(UnrepresentableCode, metric, windowIndex));

            return null;
        }
        catch (DivideByZeroException)
        {
            diagnostics.Add(new WalkForwardMetricDiagnostic(UnrepresentableCode, metric, windowIndex));

            return null;
        }
    }
}