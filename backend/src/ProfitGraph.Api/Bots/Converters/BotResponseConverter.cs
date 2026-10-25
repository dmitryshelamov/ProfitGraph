using ProfitGraph.Api.Backtesting.Contracts;
using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Api.Bots.Converters;

internal static class BotResponseConverter
{
    public static BotResponse ToResponse(this BotInternal bot)
    {
        return new BotResponse(
            bot.Id,
            bot.StrategyId,
            bot.StrategyVersionId,
            bot.CredentialId,
            bot.Venue,
            bot.Symbol,
            bot.Timeframe,
            bot.Parameters.ToDictionary(pair => pair.Key, pair => ExactGraphNumberParser.Format(pair.Value), StringComparer.Ordinal),
            ExactGraphNumberParser.Format(bot.AllocationCap),
            ExactGraphNumberParser.Format(bot.MaxOrderQuote),
            ExactGraphNumberParser.Format(bot.DailyLossLimitQuote),
            ExactGraphNumberParser.Format(bot.LimitFeeRate),
            ExactGraphNumberParser.Format(bot.MarketFeeRate),
            bot.FeeRatesReadAt,
            bot.AllowForeignOrders,
            bot.DesiredState,
            bot.RuntimeStatus,
            bot.StatusReason,
            bot.Diagnostics,
            bot.RestartAttempts,
            bot.NextRestartAt,
            bot.HasRun,
            bot.CreatedAt,
            bot.UpdatedAt,
            bot.SellerFees is { } fees
                ? new SellerFeeScheduleResponse([.. fees.Rates.Select(ExactGraphNumberParser.Format)], ExactGraphNumberParser.Format(fees.Minimum))
                : null);
    }
}