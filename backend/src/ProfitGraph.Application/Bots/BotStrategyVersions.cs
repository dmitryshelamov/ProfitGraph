using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Bots;

internal static class BotStrategyVersions
{
    /// <summary>
    ///     The latest saved version of the bot's strategy, which a start and a running graph change select.
    /// </summary>
    internal static async Task<StrategyVersion> Latest(this IStrategyRepository strategies, Bot bot, CancellationToken cancellationToken)
    {
        Strategy strategy = await strategies.Get(bot.StrategyId, cancellationToken)
            ?? throw new ResourceNotFoundException("The bot's strategy does not exist.", "strategy_not_found");

        return await strategies.GetVersion(strategy.Id, strategy.CurrentVersion, cancellationToken)
            ?? throw new ResourceNotFoundException("The strategy version does not exist.", "strategy_version_not_found");
    }
}