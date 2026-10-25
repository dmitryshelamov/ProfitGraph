using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Every <see cref="TInvestOptions.WaitingRecheckInterval" /> reads the trading status and the price band of the orders
///     the module holds locally and sends those that may go (ADR 0023, T12). Only a bot whose runner in this process reads
///     its updates is considered, and only when the core still lets it submit: a stopped, killed or degraded bot never sends
///     because a band moved or a session opened. A failure is logged by kind only and the order stays waiting.
/// </summary>
internal sealed class TInvestWaitingOrdersWorker(
    ITInvestOrderAttempts attempts,
    ITInvestBotContexts contexts,
    ITInvestOrderWaits waits,
    TInvestOrderStreams streams,
    TInvestOrderGateway gateway,
    TInvestOrderUpdates updates,
    IOptions<TInvestOptions> options,
    TimeProvider clock,
    ILogger<TInvestWaitingOrdersWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Round(stoppingToken);

            try
            {
                await Task.Delay(options.Value.WaitingRecheckInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    ///     One pass over the waiting orders, bot by bot.
    /// </summary>
    public async Task Round(CancellationToken cancellationToken)
    {
        IReadOnlyList<TInvestOrderAttempt> waiting;

        try
        {
            waiting = await attempts.Waiting(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("The T-Invest waiting orders could not be read: {Failure}", exception.GetType().Name);

            return;
        }

        foreach (IGrouping<Guid, TInvestOrderAttempt> bot in waiting.GroupBy(attempt => attempt.BotId))
        {
            var botId = new BotId(bot.Key);

            if (!streams.IsOpen(botId))
            {
                continue;
            }

            try
            {
                if (!await waits.MayRelease(botId, cancellationToken))
                {
                    continue;
                }

                TInvestBotContext context = await contexts.Get(botId, cancellationToken);

                foreach (TInvestOrderAttempt attempt in bot)
                {
                    if (await gateway.Release(context, attempt, _ => Task.CompletedTask, cancellationToken) is { } rejection)
                    {
                        updates.Publish(botId, [rejection]);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("A T-Invest bot's waiting orders could not be moved along: {Failure}", exception.GetType().Name);
            }
        }
    }
}