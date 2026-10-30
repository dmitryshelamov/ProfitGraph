using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.PositionCloses;

namespace ProfitGraph.Application.Bots.RequestPositionClose;

/// <summary>
///     Accepts a close of an open long position of a running bot. The position is read from the bot's checkpoint: a bot without
///     an open position, or with only a remainder its engine counts as flat, has nothing to close; a short is refused until a
///     close can buy it back by quantity.
/// </summary>
internal sealed class RequestPositionCloseCommandHandler(
    IBotRepository bots,
    IBotCheckpointRepository checkpoints,
    IBotPositionCloseRepository closes,
    TimeProvider clock) : IRequestHandler<RequestPositionCloseCommand, BotPositionCloseInternal>
{
    public async Task<BotPositionCloseInternal> Handle(RequestPositionCloseCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        Bot bot = await bots.GetOwned(command.BotId, command.OwnerId, cancellationToken);
        bot.EnsureAcceptsManualOrders();
        BotCheckpoint? checkpoint = await checkpoints.Get(bot.Id, cancellationToken);

        if (checkpoint?.Engine.Account.BaseTotal < 0m)
        {
            throw new StateConflictException("Closing a short position is not supported yet.", PositionCloseRequests.ShortCode);
        }

        if (checkpoint is not { Engine.PositionOpenedAt: not null, Engine.Account.BaseTotal: > 0m })
        {
            throw new StateConflictException("The bot has no open position.", PositionCloseRequests.NoPositionCode);
        }

        if (await closes.GetActive(bot.Id, cancellationToken) is { Status: BotPositionCloseStatus.Pending } waiting &&
            await closes.GetForUpdate(waiting.Id, cancellationToken) is { } locked && locked.Expire(now))
        {
            await closes.Update(locked, cancellationToken);
        }

        var close = BotPositionClose.Create(BotPositionCloseId.New(), bot.Id, command.StopAfter, now);

        if (!await closes.TryAdd(close, cancellationToken))
        {
            throw new StateConflictException("A position close of this bot has not ended yet.", PositionCloseRequests.PendingCode);
        }

        return close.ToInternal();
    }
}