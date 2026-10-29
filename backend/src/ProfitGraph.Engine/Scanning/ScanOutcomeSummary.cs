namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     One outcome over the signals that have it: how many, the share above zero (0 to 1), the mean and the median, in percent.
/// </summary>
public sealed record ScanOutcomeSummary(int Count, decimal? RisingShare, decimal? Mean, decimal? Median);