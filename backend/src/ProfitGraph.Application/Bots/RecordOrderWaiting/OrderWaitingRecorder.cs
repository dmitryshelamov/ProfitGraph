using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.RecordOrderWaiting;

/// <summary>
///     Starts, changes or ends a bot's wait for one order. The caller's transaction serializes concurrent reports.
/// </summary>
internal static class OrderWaitingRecorder
{
    public static async Task Record(
        IBotOrderWaitingRepository waiting,
        BotId botId,
        long engineOrderId,
        OrderWaitingCode? code,
        DateTimeOffset now,
        DateTimeOffset? until,
        decimal? low,
        decimal? high,
        CancellationToken cancellationToken)
    {
        BotOrderWaiting? current = await waiting.GetForUpdate(botId, engineOrderId, cancellationToken);

        if (code is not { } reason)
        {
            if (current is not null)
            {
                current.End(now);
                await waiting.Delete(current, cancellationToken);
            }

            return;
        }

        if (current is null)
        {
            await waiting.Save(BotOrderWaiting.Start(botId, engineOrderId, reason, now, until, low, high), cancellationToken);
        }
        else if (current.Change(reason, now, until, low, high))
        {
            await waiting.Save(current, cancellationToken);
        }
    }
}