namespace ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

internal static class ScannerLockQueries
{
    /// <summary>
    ///     A session advisory lock: it lasts while the holder's connection stays open, so one Worker scans at a time.
    /// </summary>
    internal const string TryAcquire = "SELECT pg_try_advisory_lock(hashtextextended('profitgraph.scanner', 0))";

    internal const string Check = "SELECT 1";
}