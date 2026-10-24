namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Детерминированное округление decimal: вниз к шагу для количеств и цен, вверх к точности для комиссий.
/// </summary>
public static class DecimalRounding
{
    public static decimal FloorToStep(decimal value, decimal step)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);

        return decimal.Floor(value / step) * step;
    }

    public static decimal CeilToPrecision(decimal value, int decimals)
    {
        return decimal.Round(value, decimals, MidpointRounding.ToPositiveInfinity);
    }
}