namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Комиссия = валовое получаемое количество × ставка, округлённая вверх до точности актива и не больше получаемого.
///     Нулевая ставка даёт ровно ноль. Покупка удерживает базовый актив, продажа — котируемый.
/// </summary>
public static class FeeCalculator
{
    public static decimal Calculate(decimal grossReceived, decimal rate, int precision)
    {
        if (rate == 0m)
        {
            return 0m;
        }

        return Math.Min(DecimalRounding.CeilToPrecision(grossReceived * rate, precision), grossReceived);
    }
}