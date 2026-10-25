using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Every <see cref="TInvestOptions.WaitingRecheckInterval" /> carries working DAY orders over the end of their trading day,
///     then reads the trading status and the price band of the orders the module holds locally and sends those that may go
///     (ADR 0023, T12). Only a bot whose runner in this process reads
///     its updates is considered, and only when the core still lets it submit: a stopped, killed or degraded bot never sends
///     because a band moved or a session opened. A failure is logged by kind only and the order stays waiting.
/// </summary>
internal sealed class TInvestWaitingOrdersWorker(
    ITInvestOrderAttempts attempts,
    ITInvestBotContexts contexts,
    ITInvestOrderWaits waits,
    TInvestOrderStreams streams,
    TInvestOrderGateway gateway,
    TInvestSchedules schedules,
    TInvestLateCommissions commissions,
    TInvestOrderUpdates updates,
    IOptions<TInvestOptions> options,
    TimeProvider clock,
    ILogger<TInvestWaitingOrdersWorker> logger) : BackgroundService
{
    private DateTimeOffset _commissionsDueAt = DateTimeOffset.MinValue;

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
    ///     One pass: carry-over of working DAY orders near their trading day's end, then the waiting orders, bot by bot, and
    ///     every <see cref="TInvestOptions.CommissionRecheckInterval" /> the late commissions.
    /// </summary>
    public async Task Round(CancellationToken cancellationToken)
    {
        await CarryOver(cancellationToken);
        await Release(cancellationToken);

        DateTimeOffset now = clock.GetUtcNow();

        if (now >= _commissionsDueAt)
        {
            _commissionsDueAt = now + options.Value.CommissionRecheckInterval;
            await commissions.Round(options.Value.CommissionPendingWindow, cancellationToken);
        }
    }

    /// <summary>
    ///     <see cref="TInvestOptions.CarryOverLead" /> before the end of the current trading day's last exchange interval, each
    ///     working DAY limit attempt of a reading bot is cancelled and its remainder carried to the next trading date. A day
    ///     the schedule does not know, or no schedule, plans nothing: such an order expires at the exchange and its bot
    ///     degrades with an unresolved order for the owner.
    /// </summary>
    private async Task CarryOver(CancellationToken cancellationToken)
    {
        IReadOnlyList<TInvestOrderAttempt> working;

        try
        {
            working = await attempts.Working(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("The T-Invest working orders could not be read: {Failure}", exception.GetType().Name);

            return;
        }

        DateTimeOffset now = clock.GetUtcNow();

        foreach (IGrouping<Guid, TInvestOrderAttempt> bot in working.Where(attempt => attempt is { Type: OrderType.Limit, CancelledBy: not TInvestCancelReason.Owner })
                     .GroupBy(attempt => attempt.BotId))
        {
            var botId = new BotId(bot.Key);

            if (!streams.IsOpen(botId))
            {
                continue;
            }

            try
            {
                TInvestBotContext context = await contexts.Get(botId, cancellationToken);

                if (await schedules.Days(context, cancellationToken) is not { } days ||
                    TInvestSchedules.Current(days, now) is not { Known: true, End: { } end } day ||
                    now < end - options.Value.CarryOverLead)
                {
                    continue;
                }

                foreach (TInvestOrderAttempt attempt in bot)
                {
                    await gateway.CarryOver(context, attempt, day.Date, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("A T-Invest bot's day orders could not be carried over: {Failure}", exception.GetType().Name);
            }
        }
    }

    private async Task Release(CancellationToken cancellationToken)
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