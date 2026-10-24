namespace ProfitGraph.Application.Backtesting.Execution;

/// <summary>
///     Section <c>Backtests</c>: how many replay records may wait for storage, and how many are committed together.
/// </summary>
public sealed class BacktestExecutionOptions
{
    public const string SectionName = "Backtests";

    public int ChannelCapacity { get; init; } = 4096;
    public int BatchSize { get; init; } = 1000;

    public bool IsValid()
    {
        return BatchSize is > 0 and <= 10_000 && ChannelCapacity >= BatchSize && ChannelCapacity <= 1_000_000;
    }
}