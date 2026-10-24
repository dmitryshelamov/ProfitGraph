using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.ChangeBotRiskLimits;

internal sealed class ChangeBotRiskLimitsCommandHandler(IBotRepository bots, TimeProvider clock) : IRequestHandler<ChangeBotRiskLimitsCommand, BotInternal>
{
    public async Task<BotInternal> Handle(ChangeBotRiskLimitsCommand command, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwnedForUpdate(command.Id, command.OwnerId, cancellationToken);
        bot.ChangeRiskLimits(new BotRiskLimits(command.MaxOrderQuote, command.DailyLossLimitQuote), clock.GetUtcNow());
        await bots.Update(bot, cancellationToken);

        return bot.ToInternal();
    }
}