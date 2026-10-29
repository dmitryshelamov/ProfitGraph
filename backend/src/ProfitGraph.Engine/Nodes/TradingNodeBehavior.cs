namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Trusted production behavior frozen into the trading compilation plan.
/// </summary>
public enum TradingNodeBehavior
{
    Balance,
    Position,
    ActiveOrder,
    HasActiveOrders,
    OrderSizePreview,
    OnOrderFilled,
    OnOrderCancelled,
    OnOrderRejected,
    EventFields,
    MarketBuy,
    LimitBuy,
    MarketSell,
    LimitSell,
    CancelOrder,
    Grid,
    CancelGrid,
    MarketCover,
    LimitCover
}