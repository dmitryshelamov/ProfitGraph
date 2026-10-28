namespace ProfitGraph.Domain.Optimization.Search;

/// <summary>
///     The mixing step of SplitMix64; with its increment it is part of the search reproducibility contract
///     (Profit Fox Designer ADR 0033).
/// </summary>
internal static class SplitMix64
{
    internal const ulong Increment = 0x9E3779B97F4A7C15UL;

    internal static ulong Mix(ulong value)
    {
        unchecked
        {
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;

            return value ^ (value >> 31);
        }
    }
}