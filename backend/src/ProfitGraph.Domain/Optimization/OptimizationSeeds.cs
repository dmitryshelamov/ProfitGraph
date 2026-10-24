namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     Derived seeds of a frozen seed, for each TPE batch and each walk-forward window (Profit Fox Designer ADR 0033).
/// </summary>
public static class OptimizationSeeds
{
    /// <summary>
    ///     The (<paramref name="index" /> + 1)-th SplitMix64 word from the frozen seed, in constant time. Unsigned state
    ///     arithmetic wraps modulo 2^64, as in <see cref="OptimizationRandom" />.
    /// </summary>
    public static ulong Derive(ulong seed, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        return SplitMix64.Mix(unchecked(seed + ((ulong)index + 1UL) * SplitMix64.Increment));
    }
}