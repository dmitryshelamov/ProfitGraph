namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     The order of a preset's result: by one metric, empty values last.
/// </summary>
public sealed record ScanSort(ScanMetric Metric, bool Descending);