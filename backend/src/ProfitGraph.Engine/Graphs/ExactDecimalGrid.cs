using System.Numerics;

namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Shared exact decimal grid arithmetic for optimization enumeration and engine parameter selection.
/// </summary>
internal static class ExactDecimalGrid
{
    public static int Scale(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);

        return (bits[3] >> 16) & 0xff;
    }

    public static BigInteger ToScaledInteger(decimal value, int scale)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        BigInteger coefficient = (uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);

        if (bits[3] < 0)
        {
            coefficient = -coefficient;
        }

        return coefficient * BigInteger.Pow(value: 10, scale - Scale(value));
    }

    public static bool IsOnGrid(decimal selected, decimal min, decimal step)
    {
        if (step <= 0m)
        {
            return false;
        }

        // Subtraction is exact when one operand is zero, or equal-scale operands have the same sign.
        // Other cases can overflow or silently round, so they use scaled integer arithmetic.
        if (min == 0m || selected == 0m || (Math.Sign(selected) == Math.Sign(min) && Scale(selected) == Scale(min)))
        {
            return (selected - min) % step == 0m;
        }

        int scale = Math.Max(Scale(selected), Math.Max(Scale(min), Scale(step)));

        return (ToScaledInteger(selected, scale) - ToScaledInteger(min, scale)) % ToScaledInteger(step, scale) == BigInteger.Zero;
    }
}