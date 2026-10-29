namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Deterministic square root in decimal arithmetic only, so replay never depends on floating-point hardware.
/// </summary>
/// <remarks>
///     Newton's iteration x = (x + v / x) / 2 starts from an upper bound found by scaling v into [1, 100) by powers of 100.
///     From above, the iterates fall monotonically, so the loop stops at the first one that does not fall. The result is
///     within a few units in the last of decimal's 28–29 significant digits of the exact root; exact squares give exact roots.
/// </remarks>
internal static class DecimalSquareRoot
{
    public static decimal Sqrt(decimal value)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Square root of a negative value.");
        }

        if (value == 0m)
        {
            return 0m;
        }

        decimal scaled = value;
        var scale = 1m;

        while (scaled >= 100m)
        {
            scaled /= 100m;
            scale *= 10m;
        }

        while (scaled < 1m)
        {
            scaled *= 100m;
            scale /= 10m;
        }

        // (1 + s) / 2 >= sqrt(s) for every s; one Newton step from any positive guess lands at or above the root.
        decimal root = scale * ((1m + scaled) / 2m);
        root = (root + value / root) / 2m;

        while (true)
        {
            decimal next = (root + value / root) / 2m;

            if (next >= root)
            {
                return root;
            }

            root = next;
        }
    }
}