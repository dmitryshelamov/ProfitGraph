using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Strategies;

internal static class LiveImpact
{
    public const string ConflictCode = "live_impact";

    public static IReadOnlyList<LiveImpactBotInternal> Of(IReadOnlyList<Bot> bots)
    {
        return
        [
            .. bots.Select(bot => new LiveImpactBotInternal(
                bot.Id.Value,
                bot.Series.Venue.Value,
                bot.Series.Symbol.Value,
                bot.Series.Timeframe.Key,
                bot.StrategyVersionId.Value,
                bot.RuntimeStatus))
        ];
    }
}