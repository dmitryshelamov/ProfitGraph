using MediatR;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.ApplyBotReconciliation;

internal sealed class ApplyBotReconciliationCommandHandler(IBotRepository bots, IBotLeaseRepository leases, IOrderIntentRepository intents, TimeProvider clock)
    : IRequestHandler<ApplyBotReconciliationCommand, bool>
{
    public async Task<bool> Handle(ApplyBotReconciliationCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        var botId = new BotId(command.BotId);
        BotLease? lease = await leases.GetForUpdate(botId, cancellationToken);

        if (lease is null || !lease.IsHeldBy(new LeaseHolderId(command.HolderId), command.Epoch, now))
        {
            return false;
        }

        Bot bot = await bots.GetForUpdate(botId, cancellationToken) ?? throw new InvalidOperationException("A leased bot does not exist.");
        HashSet<string> found = [.. command.FoundClientOrderIds];
        HashSet<string> final = [.. command.FinalClientOrderIds];

        IReadOnlyDictionary<string, IReadOnlyList<string>> proven = command.ProvenAbsences ?? new Dictionary<string, IReadOnlyList<string>>();

        foreach (OrderIntent listed in await intents.ListByBot(botId, cancellationToken))
        {
            bool undelivered = listed.Status is OrderIntentStatus.Pending or OrderIntentStatus.Sending or OrderIntentStatus.Failed;
            bool closed = listed.Kind == OrderIntentKind.Submit && proven.ContainsKey(listed.ClientOrderId.Value);

            if (!undelivered && !closed)
            {
                continue;
            }

            OrderIntent intent = await intents.GetForUpdate(listed.Id, cancellationToken) ?? throw new InvalidOperationException("A listed intent does not exist.");
            var changedIntent = false;

            if (undelivered && Resolves(intent, found, final))
            {
                intent.Resolve(now);
                changedIntent = true;
            }
            else if (undelivered && intent.ResetUnsentFailure())
            {
                changedIntent = true;
            }

            if (closed && intent is { Status: OrderIntentStatus.Sent, ConfirmedAbsentAt: null, PlaceStartedAt: not null })
            {
                intent.ConfirmExpiredAutomatically(proven[intent.ClientOrderId.Value], now);
                changedIntent = true;
            }

            if (changedIntent)
            {
                await intents.Update(intent, cancellationToken);
            }
        }

        bool changed = bot.MarkExchangeUnavailable(unavailable: false, now);

        if (command.Degradation is { } reason)
        {
            changed |= bot.Degrade(reason, now, command.DegradationDetail);
        }

        if (changed)
        {
            await bots.Update(bot, cancellationToken);
        }

        return true;
    }

    /// <summary>
    ///     A submit the venue knows was delivered; a cancel of an order that can no longer change is not needed. A cancel
    ///     being delivered now is left to its delivery.
    /// </summary>
    private static bool Resolves(OrderIntent intent, HashSet<string> found, HashSet<string> final)
    {
        return intent.Kind == OrderIntentKind.Submit
            ? found.Contains(intent.ClientOrderId.Value)
            : intent.Status != OrderIntentStatus.Sending && final.Contains(intent.ClientOrderId.Value);
    }
}