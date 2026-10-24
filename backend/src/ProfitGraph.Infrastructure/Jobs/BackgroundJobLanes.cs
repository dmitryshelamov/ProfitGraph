namespace ProfitGraph.Infrastructure.Jobs;

/// <summary>
///     The Worker runs one job at a time in each lane, so a long optimization never holds back backtests and downloads.
/// </summary>
public static class BackgroundJobLanes
{
    public const string Main = "main";
    public const string Optimization = "optimization";

    internal static IReadOnlyList<string> All { get; } = [Main, Optimization];
}