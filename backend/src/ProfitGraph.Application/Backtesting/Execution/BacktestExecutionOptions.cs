namespace ProfitGraph.Application.Backtesting.Execution;

/// <summary>
///     Section <c>Backtests</c>: how many replay records may wait for storage, how many are committed together, and when
///     finished runs are compacted.
/// </summary>
public sealed class BacktestExecutionOptions
{
    public const string SectionName = "Backtests";

    public int ChannelCapacity { get; init; } = 4096;
    public int BatchSize { get; init; } = 1000;

    /// <summary>
    ///     How long a finished run keeps its per-candle observations, so live clients can catch up, before the Worker
    ///     turns them into chart buckets.
    /// </summary>
    public TimeSpan CompactAfter { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan CompactionInterval { get; init; } = TimeSpan.FromSeconds(30);

    public bool IsValid()
    {
        return BatchSize is > 0 and <= 10_000 && ChannelCapacity >= BatchSize && ChannelCapacity <= 1_000_000 &&
            CompactAfter >= TimeSpan.Zero && CompactionInterval > TimeSpan.Zero;
    }
}