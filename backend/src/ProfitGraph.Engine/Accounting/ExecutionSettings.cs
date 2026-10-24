namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Параметры прогона, влияющие на исполнение: стартовый капитал в котируемой валюте, проскальзывание рыночных ордеров
///     и ставки комиссии лимитных и рыночных исполнений. Ставки и проскальзывание — доли в [0, 1).
/// </summary>
public sealed record ExecutionSettings
{
    public decimal StartingCapital { get; }
    public decimal Slippage { get; }
    public decimal LimitFeeRate { get; }
    public decimal MarketFeeRate { get; }

    public ExecutionSettings(decimal startingCapital, decimal slippage, decimal limitFeeRate, decimal marketFeeRate)
    {
        if (startingCapital <= 0m)
        {
            throw new TradingInvariantException("Starting capital must be positive.");
        }

        if (!IsFraction(slippage) || !IsFraction(limitFeeRate) || !IsFraction(marketFeeRate))
        {
            throw new TradingInvariantException("Slippage and fee rates must be in [0, 1).");
        }

        StartingCapital = startingCapital;
        Slippage = slippage;
        LimitFeeRate = limitFeeRate;
        MarketFeeRate = marketFeeRate;
    }

    public decimal FeeRate(OrderType type)
    {
        return type == OrderType.Limit ? LimitFeeRate : MarketFeeRate;
    }

    private static bool IsFraction(decimal value)
    {
        return value is >= 0m and < 1m;
    }
}