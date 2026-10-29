namespace ProfitGraph.Application.Backtesting;

/// <summary>
///     The stable transport names of the run list's statuses and origins.
/// </summary>
public static class BacktestRankingNames
{
    public static IReadOnlyList<string> Statuses { get; } = ["completed", "failed", "cancelled"];

    public static IReadOnlyList<string> Origins { get; } = ["manual", "optimization"];

    public static string Status(BacktestRankingStatus status)
    {
        return status switch
        {
            BacktestRankingStatus.Completed => "completed",
            BacktestRankingStatus.Failed => "failed",
            BacktestRankingStatus.Cancelled => "cancelled",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }

    public static BacktestRankingStatus? ParseStatus(string status)
    {
        return status switch
        {
            "completed" => BacktestRankingStatus.Completed,
            "failed" => BacktestRankingStatus.Failed,
            "cancelled" => BacktestRankingStatus.Cancelled,
            _ => null
        };
    }

    public static string Origin(BacktestRankingOrigin origin)
    {
        return origin switch
        {
            BacktestRankingOrigin.Manual => "manual",
            BacktestRankingOrigin.Optimization => "optimization",
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
    }

    public static BacktestRankingOrigin? ParseOrigin(string origin)
    {
        return origin switch
        {
            "manual" => BacktestRankingOrigin.Manual,
            "optimization" => BacktestRankingOrigin.Optimization,
            _ => null
        };
    }
}