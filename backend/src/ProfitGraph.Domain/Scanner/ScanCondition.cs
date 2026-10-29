namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     Keeps the pairs whose metric lies within the bounds, both inclusive; a pair whose metric is empty fails the condition. At
///     least one bound is set, and the minimum is not above the maximum.
/// </summary>
public sealed record ScanCondition
{
    public ScanMetric Metric { get; }
    public decimal? Minimum { get; }
    public decimal? Maximum { get; }

    public ScanCondition(ScanMetric metric, decimal? minimum, decimal? maximum)
    {
        if (!Enum.IsDefined(metric) || (minimum is null && maximum is null) || minimum > maximum)
        {
            throw new ScanPresetRuleException(
                ScanPresetRuleException.InvalidCondition,
                "A scanner condition names a known metric and at least one bound, with the minimum not above the maximum.");
        }

        Metric = metric;
        Minimum = minimum;
        Maximum = maximum;
    }
}