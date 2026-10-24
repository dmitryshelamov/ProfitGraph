namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The fee rates the bot's engine account uses for limit and market orders, read from the venue at creation and at
///     every start, with the UTC time they were read. Each rate is in [0, 1) with at most twelve decimal places.
/// </summary>
public sealed record BotFeeRates
{
    private const int MaxDecimals = 12;

    public decimal LimitRate { get; }
    public decimal MarketRate { get; }
    public DateTimeOffset ReadAt { get; }

    public BotFeeRates(decimal limitRate, decimal marketRate, DateTimeOffset readAt)
    {
        if (!IsRate(limitRate) || !IsRate(marketRate) || readAt.Offset != TimeSpan.Zero)
        {
            throw new BotRuleException(BotRuleException.InvalidSettings, "Fee rates must be in [0, 1) with at most twelve decimals, read at a UTC time.");
        }

        LimitRate = limitRate;
        MarketRate = marketRate;
        ReadAt = readAt;
    }

    public bool HasSameRates(BotFeeRates other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return LimitRate == other.LimitRate && MarketRate == other.MarketRate;
    }

    private static bool IsRate(decimal rate)
    {
        return rate is >= 0m and < 1m && decimal.Round(rate, MaxDecimals) == rate;
    }
}