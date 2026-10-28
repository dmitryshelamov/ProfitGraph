namespace ProfitGraph.Application.Optimization.Execution;

/// <summary>
///     Section <c>Optimization</c>: how many trials replay at once, how many results may wait for the receiver and how
///     many trials are stored together.
/// </summary>
public sealed class OptimizationTrialRunnerOptions
{
    public const string SectionName = "Optimization";

    public int MaxWorkers { get; init; } = 8;
    public int ChannelCapacity { get; init; } = 256;
    public int BatchSize { get; init; } = 500;

    /// <summary>
    ///     The longest time the receiver goes without offering a partial batch for storage.
    /// </summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(1);

    public bool IsValid()
    {
        return MaxWorkers is > 0 and <= 256 && ChannelCapacity is > 0 and <= 1_000_000 && BatchSize is > 0 and <= 10_000 && FlushInterval > TimeSpan.Zero;
    }
}