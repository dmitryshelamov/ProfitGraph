using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.ConfirmOrderAbsent;

internal sealed class ConfirmOrderAbsentCommandHandler(
    IBotRepository bots,
    IOrderIntentRepository intents,
    IBotCheckpointRepository checkpoints,
    VenueRegistry venues,
    TimeProvider clock) : IRequestHandler<ConfirmOrderAbsentCommand>
{
    public async Task Handle(ConfirmOrderAbsentCommand command, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwnedForUpdate(command.BotId, command.OwnerId, cancellationToken);
        OrderIntent? intent = command.IntentId == Guid.Empty ? null : await intents.GetForUpdate(new OrderIntentId(command.IntentId), cancellationToken);

        if (intent is null || intent.BotId != bot.Id)
        {
            throw new ResourceNotFoundException("The bot has no such order.", BotErrorCodes.OrderIntentNotFound);
        }

        if (!OrderAbsence.CanConfirm(bot, intent, await checkpoints.Get(bot.Id, cancellationToken), venues.Find(bot.Series.Venue.Value)))
        {
            throw new StateConflictException(
                "Only an unfilled submit whose delivery stopped the bot can be confirmed absent, on a venue that checks it.",
                BotErrorCodes.AbsenceNotConfirmable);
        }

        intent.ConfirmAbsent(new UserId(command.OwnerId), clock.GetUtcNow());
        await intents.Update(intent, cancellationToken);
    }
}