using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.OrderIntents;

namespace ProfitGraph.Application.Bots.RecheckUnresolvedOrders;

/// <summary>
///     Reads the bot, its checkpoint and its intents, asks the venue's gateway whether it can prove every unresolved order
///     absent, and only then locks the bot to clear the reason. Nothing is sent to the venue. A bot the owner stopped, one that
///     stopped for another reason, a venue without a gateway or without proofs, and any read that fails leave the bot as it is.
/// </summary>
internal sealed class RecheckUnresolvedOrdersCommandHandler(
    IBotRepository bots,
    IBotCheckpointRepository checkpoints,
    IOrderIntentRepository intents,
    VenuePorts<IOrderGateway> gateways,
    TimeProvider clock) : IRequestHandler<RecheckUnresolvedOrdersCommand, bool>
{
    public async Task<bool> Handle(RecheckUnresolvedOrdersCommand command, CancellationToken cancellationToken)
    {
        var botId = new BotId(command.BotId);

        if (await bots.Get(botId, cancellationToken) is not
                { DesiredState: BotDesiredState.Running, IsDegraded: true, StatusReason: BotStatusReason.UnresolvedOrder } bot ||
            gateways.Find(bot.Series.Venue) is not { } gateway)
        {
            return false;
        }

        BotCheckpoint? checkpoint = await checkpoints.Get(botId, cancellationToken);
        BotReconcileInternal request = ReconcileRequests.For(bot, checkpoint?.Engine, gateway, await intents.ListByBot(botId, cancellationToken));

        if (request.Orders.Count == 0 || !await gateway.CanProveAbsence(request, cancellationToken))
        {
            return false;
        }

        if (await bots.GetForUpdate(botId, cancellationToken) is not { } locked || !locked.ClearUnresolvedOrder(clock.GetUtcNow()))
        {
            return false;
        }

        await bots.Update(locked, cancellationToken);

        return true;
    }
}
