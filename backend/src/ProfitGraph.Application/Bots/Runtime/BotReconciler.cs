using MediatR;

using ProfitGraph.Application.Bots.ApplyBotReconciliation;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     Reconciles an opened bot with its venue before its submit gate opens (ADR 0016, Profit Fox Designer ADR 0055): it
///     looks up every engine order that is not final and every submit or cancel whose delivery is unknown, applies what the
///     venue reports to the engine and records the outcome. A failed cancel of an order that is still active, or a safety
///     reason from the venue, degrades the bot; a venue that cannot answer leaves the bot to retry. An order the gateway
///     closed on its own evidence (ADR 0030) is recorded with the checks that justified it.
/// </summary>
public sealed class BotReconciler(IOrderIntentRepository intents, ISender sender)
{
    public async Task<BotReconcileResult> Reconcile(
        Bot bot,
        LeaseHolderId holder,
        long epoch,
        BotEngineSession session,
        IOrderGateway gateway,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OrderIntent> stored = await intents.ListByBot(bot.Id, cancellationToken);
        BotReconciliationInternal reconciliation;

        try
        {
            reconciliation = await gateway.Reconcile(
                ReconcileRequests.For(bot, session.Checkpoint, gateway, stored),
                cancellationToken);
        }
        catch (OrderGatewaySafetyException exception)
        {
            reconciliation = new BotReconciliationInternal([], [], exception.Reason, exception.Detail);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BotReconcileResult.Unavailable;
        }

        foreach (ExternalOrderUpdate update in reconciliation.Updates)
        {
            await session.ProcessExternalUpdate(update, cancellationToken);
        }

        HashSet<string> active = [.. reconciliation.Found.Where(order => !order.IsFinal).Select(order => order.ClientOrderId.Value)];
        BotStatusReason? degradation = reconciliation.Degradation ??
            (stored.Any(intent => intent is { Kind: OrderIntentKind.Cancel, Status: OrderIntentStatus.Failed } && active.Contains(intent.ClientOrderId.Value))
                ? BotStatusReason.UnresolvedOrder
                : null);
        var applied = new ApplyBotReconciliationCommand(
            bot.Id.Value,
            holder.Value,
            epoch,
            [.. reconciliation.Found.Select(order => order.ClientOrderId.Value)],
            [.. reconciliation.Found.Where(order => order.IsFinal).Select(order => order.ClientOrderId.Value)],
            degradation,
            reconciliation.Degradation is null ? null : reconciliation.DegradationDetail,
            reconciliation.Found.Where(order => order.AbsenceProof is not null)
                .ToDictionary(order => order.ClientOrderId.Value, order => order.AbsenceProof!.Checks));

        if (!await sender.Send(applied, cancellationToken))
        {
            throw new BotLeaseLostException();
        }

        return new BotReconcileResult(VenueAnswered: true, degradation);
    }
}