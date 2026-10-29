namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

/// <summary>
///     A remembered pair in <c>scan_alert_watches.pairs</c>, keyed by its symbol.
/// </summary>
internal sealed class ScanAlertPairDb
{
    public bool InResult { get; init; }
    public DateTimeOffset? AlertedAt { get; init; }
}