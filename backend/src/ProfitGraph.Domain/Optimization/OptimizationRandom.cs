using System.Numerics;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     SplitMix64 v1: the frozen seed is the initial state, each word advances it by the increment and mixes it. Words
///     join least significant first; values outside the bound are rejected rather than reduced, so there is no modulo
///     bias. The algorithm and word order are a reproducibility contract.
/// </summary>
internal sealed class OptimizationRandom(ulong seed)
{
    private ulong _state = seed;

    public BigInteger NextBelow(BigInteger bound)
    {
        if (bound <= BigInteger.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(bound));
        }

        var bits = checked((int)(bound - BigInteger.One).GetBitLength());
        BigInteger mask = (BigInteger.One << bits) - BigInteger.One;
        BigInteger value;

        do
        {
            value = BigInteger.Zero;

            for (var offset = 0; offset < bits; offset += 64)
            {
                value |= (BigInteger)NextWord() << offset;
            }

            value &= mask;
        }
        while (value >= bound);

        return value;
    }

    private ulong NextWord()
    {
        unchecked
        {
            _state += SplitMix64.Increment;

            return SplitMix64.Mix(_state);
        }
    }
}