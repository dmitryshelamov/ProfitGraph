namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Приводит количество вниз к шагу и лимитную цену вниз к тику, затем проверяет минимумы.
///     Объём никогда не увеличивается до минимума: такой ордер отклоняется.
/// </summary>
public static class OrderNormalizer
{
    public static long ToLotsExact(decimal quantity, InstrumentRules rules)
    {
        if (quantity <= 0m || quantity % rules.QuantityStep != 0m)
        {
            throw new TradingInvariantException("InvalidLotQuantity: shares must contain an exact positive number of lots.");
        }

        return checked((long)(quantity / rules.QuantityStep));
    }

    public static decimal NormalizeQuantity(decimal quantity, InstrumentRules rules)
    {
        return DecimalRounding.FloorToStep(quantity, rules.QuantityStep);
    }

    public static decimal NormalizePrice(decimal price, InstrumentRules rules)
    {
        return DecimalRounding.FloorToStep(price, rules.PriceTick);
    }

    /// <summary>
    ///     Возвращает причину отказа для нормализованного количества и цены или null, если ордер допустим.
    ///     Без цены проверяется только количество: минимальный объём рыночной продажи проверяется при исполнении.
    /// </summary>
    public static RejectionReason? CheckMinimums(decimal quantity, decimal? price, InstrumentRules rules)
    {
        if (quantity <= 0m)
        {
            return RejectionReason.ZeroQuantity;
        }

        if (quantity < rules.MinQuantity)
        {
            return RejectionReason.BelowMinQuantity;
        }

        if (price is not null && quantity * price.Value < rules.MinNotional)
        {
            return RejectionReason.BelowMinNotional;
        }

        return null;
    }
}