namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Неблагоприятное проскальзывание рыночного исполнения: покупка дороже, продажа дешевле опорной цены.
///     Цена после проскальзывания не округляется к шагу цены.
/// </summary>
public static class SlippageModel
{
    public static decimal Apply(decimal referencePrice, OrderSide side, decimal slippage)
    {
        return side == OrderSide.Buy ? referencePrice * (1m + slippage) : referencePrice * (1m - slippage);
    }
}