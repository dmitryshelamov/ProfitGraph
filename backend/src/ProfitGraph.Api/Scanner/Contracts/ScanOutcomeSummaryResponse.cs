namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     One outcome over the signals that have it: how many, the share above zero (0 to 1) and the mean and median in percent, as exact
///     decimal strings.
/// </summary>
public sealed record ScanOutcomeSummaryResponse(int Count, string? RisingShare, string? Mean, string? Median);