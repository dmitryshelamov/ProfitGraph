namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A preset condition: a metric name and inclusive bounds, at least one of them set.
/// </summary>
public sealed record ScanConditionInternal(string Metric, decimal? Minimum, decimal? Maximum);