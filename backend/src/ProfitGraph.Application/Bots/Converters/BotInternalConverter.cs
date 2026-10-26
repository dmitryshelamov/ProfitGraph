using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Converters;

public static class BotInternalConverter
{
    public static BotInternal ToInternal(this Bot bot)
    {
        return new BotInternal(
            bot.Id.Value,
            bot.StrategyId.Value,
            bot.StrategyVersionId.Value,
            bot.CredentialId.Value,
            bot.Series.Venue.Value,
            bot.Series.Symbol.Value,
            bot.Series.Timeframe.Key,
            bot.Parameters.Values,
            bot.AllocationCap,
            bot.RiskLimits.MaxOrderQuote,
            bot.RiskLimits.DailyLossLimitQuote,
            bot.FeeRates.LimitRate,
            bot.FeeRates.MarketRate,
            bot.FeeRates.ReadAt,
            bot.AllowForeignOrders,
            bot.DesiredState,
            bot.RuntimeStatus,
            bot.StatusReason,
            [.. Enum.GetValues<BotDiagnostics>().Where(flag => flag != BotDiagnostics.None && bot.Diagnostics.HasFlag(flag)).Select(flag => flag.ToString())],
            bot.RestartAttempts,
            bot.NextRestartAt,
            bot.HasRun,
            bot.CreatedAt,
            bot.UpdatedAt,
            bot.FeeRates.SellerFees is { } fees ? new SellerFeeScheduleInternal([.. fees.Rates], fees.Minimum, fees.Increment) : null,
            bot.VenueSettings.Version,
            bot.VenueSettings.Json);
    }
}