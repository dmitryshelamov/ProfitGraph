using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The fee rates the bot's engine account uses for limit and market orders, read from the venue at creation and at
///     every start, with the UTC time they were read. Each rate is in [0, 1) with at most twelve decimal places. A venue
///     with marketplace fees paid by the seller gives a seller fee schedule instead, and both rates are zero (ADR 0022).
/// </summary>
public sealed record BotFeeRates
{
    private const int MaxDecimals = 12;

    public decimal LimitRate { get; }
    public decimal MarketRate { get; }
    public DateTimeOffset ReadAt { get; }
    public SellerFeeSchedule? SellerFees { get; }

    public BotFeeRates(decimal limitRate, decimal marketRate, DateTimeOffset readAt, SellerFeeSchedule? sellerFees = null)
    {
        if (!IsRate(limitRate) || !IsRate(marketRate) || readAt.Offset != TimeSpan.Zero)
        {
            throw new BotRuleException(BotRuleException.InvalidSettings, "Fee rates must be in [0, 1) with at most twelve decimals, read at a UTC time.");
        }

        if (sellerFees is not null && (limitRate != 0m || marketRate != 0m))
        {
            throw new BotRuleException(BotRuleException.InvalidSettings, "Seller fees replace the limit and market rates, which must be zero.");
        }

        LimitRate = limitRate;
        MarketRate = marketRate;
        ReadAt = readAt;
        SellerFees = sellerFees;
    }

    public bool HasSameRates(BotFeeRates other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return LimitRate == other.LimitRate && MarketRate == other.MarketRate && Equals(SellerFees, other.SellerFees);
    }

    private static bool IsRate(decimal rate)
    {
        return rate is >= 0m and < 1m && decimal.Round(rate, MaxDecimals) == rate;
    }
}