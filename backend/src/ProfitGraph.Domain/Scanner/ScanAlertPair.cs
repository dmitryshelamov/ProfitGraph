namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     What a preset's alert watch remembers of a pair: whether it was in the result at the last check and when it was last reported.
/// </summary>
public sealed record ScanAlertPair(bool InResult, DateTimeOffset? AlertedAt);