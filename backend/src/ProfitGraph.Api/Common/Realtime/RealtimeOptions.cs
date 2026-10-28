namespace ProfitGraph.Api.Common.Realtime;

/// <summary>
///     Section <c>Realtime</c>: how the API's realtime publishers page what they push and recover from a lost notification
///     stream.
/// </summary>
public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>
    ///     The pause before a publisher listens again after its notification stream failed.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How many backtest observations one read for subscribers returns.
    /// </summary>
    public int BacktestProgressPageSize { get; init; } = 100;

    /// <summary>
    ///     How many optimization trials one read for subscribers returns.
    /// </summary>
    public int OptimizationProgressPageSize { get; init; } = 500;

    public bool IsValid()
    {
        return RetryDelay > TimeSpan.Zero && BacktestProgressPageSize > 0 && OptimizationProgressPageSize > 0;
    }
}