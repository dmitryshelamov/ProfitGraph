using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Venues.TInvest.Transport;

using OrderType = ProfitGraph.Engine.Accounting.OrderType;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Keeps the outcome of DAY orders that no runner in this process reads (ADR 0031): as soon as the trading day that holds a
///     working DAY limit attempt has ended by the schedule, and then every <see cref="TInvestOptions.ClosedSessionSweepInterval" />
///     until the next session opens, it reads the attempt's state with <c>GetOrderState</c> and records a final one, with its
///     executions and, for a cancellation nobody asked for, <see cref="TInvestCancelReason.Expired" />. The next reconciliation then
///     finds the outcome in the attempts table when T-Invest no longer answers for the order. Bots of every state count: degraded,
///     failed, stopped or never started. It only reads: nothing is placed or cancelled, and nothing reaches the engine. A day the
///     schedule does not know is left alone. Reads are grouped by account; an account whose sweep cannot finish, because a read
///     failed or the schedule could not be read, is tried again after <see cref="TInvestOptions.ClosedSessionSweepFirstRetryDelay" />,
///     doubling up to <see cref="TInvestOptions.ClosedSessionSweepMaxRetryDelay" />, at most
///     <see cref="TInvestOptions.ClosedSessionSweepMaxRetries" /> times, and then waits for its next scheduled sweep.
/// </summary>
internal sealed class TInvestClosedSessionSweep(
    ITInvestOrderAttempts attempts,
    ITInvestBotContexts contexts,
    TInvestOrderStreams streams,
    TInvestOrderReads reads,
    TInvestOrderProgress progress,
    TInvestSchedules schedules,
    IOptions<TInvestOptions> options,
    TimeProvider clock,
    ILogger<TInvestClosedSessionSweep> logger) : BackgroundService
{
    private readonly Dictionary<string, TInvestSweepAccount> _accounts = new Dictionary<string, TInvestSweepAccount>(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset next = await Round(stoppingToken);
            TimeSpan pause = next - clock.GetUtcNow();

            try
            {
                await Task.Delay(
                    pause < options.Value.ClosedSessionSweepShortestPause ? options.Value.ClosedSessionSweepShortestPause :
                    pause > options.Value.ClosedSessionSweepIdle ? options.Value.ClosedSessionSweepIdle : pause,
                    clock,
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    ///     One pass over the accounts that are due. Returns when the earliest account is due next, or after the idle pause when no
    ///     account has working orders; the loop looks again within the idle pause either way.
    /// </summary>
    public async Task<DateTimeOffset> Round(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset? next = null;
        IReadOnlyList<TInvestOrderAttempt> working;

        try
        {
            working = await attempts.Working(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("The T-Invest working orders could not be read for the post-session sweep: {Failure}", exception.GetType().Name);

            return now + options.Value.ClosedSessionSweepIdle;
        }

        var accounts =
            new Dictionary<string, List<(TInvestBotContext Context, TInvestOrderAttempt Attempt)>>(StringComparer.Ordinal);

        foreach (IGrouping<Guid, TInvestOrderAttempt> bot in working.Where(attempt => attempt.Type == OrderType.Limit).GroupBy(attempt => attempt.BotId))
        {
            var botId = new BotId(bot.Key);

            if (streams.IsOpen(botId))
            {
                // The bot's runner reads its orders and hears the outcome itself.
                continue;
            }

            try
            {
                TInvestBotContext context = await contexts.Get(botId, cancellationToken);
                var key = $"{context.Token.Fingerprint}:{context.AccountId}";

                if (!accounts.TryGetValue(key, out List<(TInvestBotContext Context, TInvestOrderAttempt Attempt)>? orders))
                {
                    accounts[key] = orders = [];
                }

                orders.AddRange(bot.Select(attempt => (context, attempt)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("A T-Invest bot's account could not be read for the post-session sweep: {Failure}", exception.GetType().Name);
            }
        }

        foreach (string gone in _accounts.Keys.Except(accounts.Keys).ToList())
        {
            _accounts.Remove(gone);
        }

        foreach ((string key, List<(TInvestBotContext Context, TInvestOrderAttempt Attempt)> orders) in accounts)
        {
            TInvestSweepAccount account = _accounts.GetValueOrDefault(key) ?? new TInvestSweepAccount(DateTimeOffset.MinValue, Failures: 0);

            if (account.DueAt <= now)
            {
                (bool finished, DateTimeOffset nextEvent) = await Sweep(orders, now, cancellationToken);
                account = finished ? new TInvestSweepAccount(nextEvent, Failures: 0) : Failed(account, now);
                _accounts[key] = account;
            }

            next = next is { } earliest && earliest <= account.DueAt ? earliest : account.DueAt;
        }

        return next ?? now + options.Value.ClosedSessionSweepIdle;
    }

    /// <summary>
    ///     After a sweep that could not finish: the next try, sooner at first, or the next scheduled sweep once the tries are spent.
    /// </summary>
    private TInvestSweepAccount Failed(TInvestSweepAccount account, DateTimeOffset now)
    {
        int failures = account.Failures + 1;

        if (failures > options.Value.ClosedSessionSweepMaxRetries)
        {
            return new TInvestSweepAccount(now + options.Value.ClosedSessionSweepInterval, Failures: 0);
        }

        TimeSpan delay = options.Value.ClosedSessionSweepFirstRetryDelay * Math.Pow(x: 2, failures - 1);

        return new TInvestSweepAccount(now + (delay > options.Value.ClosedSessionSweepMaxRetryDelay ? options.Value.ClosedSessionSweepMaxRetryDelay : delay), failures);
    }

    /// <summary>
    ///     Reads and records the attempts of one account whose day has ended. Returns whether every read succeeded, and when the
    ///     account is due next: after the sweep interval while an ended day's order may still be read, or at the end of the first
    ///     day still running, whichever comes first; after the sweep interval when nothing waits.
    /// </summary>
    private async Task<(bool Finished, DateTimeOffset NextEvent)> Sweep(
        List<(TInvestBotContext Context, TInvestOrderAttempt Attempt)> orders,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DateTimeOffset regular = now + options.Value.ClosedSessionSweepInterval;
        DateTimeOffset? nextEvent = null;
        var finished = true;

        foreach (IGrouping<string, (TInvestBotContext Context, TInvestOrderAttempt Attempt)> exchange in orders.GroupBy(
                     order => order.Context.Share.Exchange,
                     StringComparer.OrdinalIgnoreCase))
        {
            TInvestBotContext first = exchange.First().Context;
            DateTimeOffset earliest = exchange.Min(order => order.Attempt.UpdatedAt);
            IReadOnlyList<TInvestTradingDay>? days = await schedules.DaysBetween(
                first,
                earliest.AddDays(-1),
                now + options.Value.ClosedSessionSweepLookahead,
                cancellationToken);

            if (days is null)
            {
                finished &= string.IsNullOrEmpty(exchange.Key);

                continue;
            }

            foreach ((TInvestBotContext context, TInvestOrderAttempt attempt) in exchange)
            {
                if (TInvestSchedules.Holding(days, attempt.UpdatedAt) is not { Known: true, End: { } end })
                {
                    continue;
                }

                if (now < end)
                {
                    nextEvent = nextEvent is { } earlier && earlier <= end ? earlier : end;

                    continue;
                }

                if ((TInvestSchedules.NextOpen(days, end) is { } open && now >= open) || now - end > options.Value.AbsenceMaxWindow)
                {
                    // The market opened again: an order T-Invest no longer answers for is left to the proof of ADR 0030.
                    continue;
                }

                finished &= await Read(context, attempt, cancellationToken);
                nextEvent = nextEvent is { } sooner && sooner <= regular ? sooner : regular;
            }
        }

        return (finished, nextEvent ?? regular);
    }

    private async Task<bool> Read(TInvestBotContext context, TInvestOrderAttempt attempt, CancellationToken cancellationToken)
    {
        try
        {
            if (await reads.ByRequestId(context, attempt.RequestId, cancellationToken) is { } state && TInvestOrderTranslation.IsFinal(state))
            {
                await progress.Record(context, attempt, state, cancellationToken);
            }

            return true;
        }
        catch (TInvestRpcException exception)
        {
            logger.LogWarning("The post-session sweep could not read a T-Invest order: {Failure}", exception.Kind);

            return false;
        }
        catch (TInvestOrderUnresolvedException exception)
        {
            logger.LogWarning("The post-session sweep could not account a T-Invest order: {Reason}", exception.Reason);

            return !exception.IsTransient;
        }
    }
}