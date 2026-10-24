using ProfitGraph.Engine.Common;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Диапазон слишком короткий, чтобы обе части in-sample / out-of-sample получили хотя бы один бар.
/// </summary>
public sealed class SampleSplitException(int barCount, decimal fraction)
    : EngineException($"Range of {barCount} bars is too short for an out-of-sample fraction of {fraction}.")
{
    public int BarCount { get; } = barCount;
    public decimal Fraction { get; } = fraction;
}