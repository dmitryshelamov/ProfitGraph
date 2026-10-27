using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Api.Strategies.Converters;

internal static class LiveImpactBotResponseConverter
{
    public static LiveImpactBotResponse ToResponse(this LiveImpactBotInternal bot)
    {
        return new LiveImpactBotResponse(
            bot.BotId,
            bot.Venue,
            bot.Symbol,
            bot.Timeframe,
            bot.StrategyVersionId,
            bot.RuntimeStatus,
            bot.Parameters.ToDictionary(pair => pair.Key, pair => ExactGraphNumberParser.Format(pair.Value), StringComparer.Ordinal));
    }
}