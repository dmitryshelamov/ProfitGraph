namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A preset condition: a metric name and inclusive bounds as exact decimal strings, at least one of them set.
/// </summary>
public sealed record ScanConditionRequest(string Metric, string? Minimum, string? Maximum);