namespace ProfitGraph.Engine.Market;

/// <summary>
///     Логическая позиция пути OHLC; границы сохраняются даже при равных ценах.
/// </summary>
public enum PricePhase
{
    Open,
    OpenToLow,
    Low,
    LowToHigh,
    High,
    HighToClose,
    Close,
    External
}