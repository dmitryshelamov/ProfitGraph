using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     The consequences of a kill (Profit Fox Designer ADR 0056), inside the caller's transaction with the bot locked: a
///     submit that never left the process is rejected with <c>KillSwitch</c>, and every order that is not final and may
///     be at the venue gets one cancel marked as a kill. The position is never sold.
/// </summary>
public sealed class BotKillSwitch(IBotCheckpointRepository checkpoints, IOrderIntentRepository intents, TimeProvider clock)
{
    /// <summary>
    ///     Applies the kill to the bot's intents. <paramref name="engine" /> is the checkpoint being committed, or null to
    ///     read the stored one.
    /// </summary>
    public async Task Apply(BotId botId, GraphEngineCheckpoint? engine, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        engine ??= (await checkpoints.Get(botId, cancellationToken))?.Engine;
        IReadOnlyList<OrderIntent> stored = await intents.ListByBot(botId, cancellationToken);
        HashSet<long> cancelled = [.. stored.Where(intent => intent.Kind == OrderIntentKind.Cancel).Select(intent => intent.EngineOrderId)];
        HashSet<long> open =
        [
            .. engine?.Account.Orders.Where(order => order.Status is OrderStatus.Active or OrderStatus.PendingSubmit or OrderStatus.PendingCancel)
                .Select(order => order.Id) ?? []
        ];

        foreach (OrderIntent listed in stored.Where(intent => intent.Kind == OrderIntentKind.Submit))
        {
            OrderIntent intent = await intents.GetForUpdate(listed.Id, cancellationToken) ?? throw new InvalidOperationException("A listed intent does not exist.");

            if (intent.RejectForKill(now))
            {
                await intents.Update(intent, cancellationToken);
            }
            else if ((intent.PlaceStartedAt is not null || intent.Status == OrderIntentStatus.Sent) && open.Contains(intent.EngineOrderId) &&
                     !cancelled.Contains(intent.EngineOrderId))
            {
                await intents.TryAdd(OrderIntent.Cancel(OrderIntentId.New(), botId, intent.EngineOrderId, intent.ClientOrderId, fromKill: true, now), cancellationToken);
            }
        }
    }
}