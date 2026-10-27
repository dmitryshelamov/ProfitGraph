using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The share's trading days from today through the next week (<c>InstrumentsService/TradingSchedules</c> for the share's
///     own exchange), classified by <see cref="TInvestScheduleVocabulary" />. Reads are kept five minutes per exchange, and a
///     failure thirty seconds, in which the schedule is unknown. A schedule never authorizes a send; it only plans carry-over
///     and marks holiday intervals closed.
/// </summary>
internal sealed class TInvestSchedules(TInvestTransport transport, TimeProvider clock, ILogger<TInvestSchedules> logger)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailureLifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (IReadOnlyList<TInvestTradingDay>? Days, DateTimeOffset ReadAt)> _schedules =
        new ConcurrentDictionary<string, (IReadOnlyList<TInvestTradingDay>? Days, DateTimeOffset ReadAt)>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     The trading days in date order, or null when the schedule cannot be read now.
    /// </summary>
    public async Task<IReadOnlyList<TInvestTradingDay>?> Days(TInvestBotContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        string exchange = context.Share.Exchange;

        if (string.IsNullOrEmpty(exchange))
        {
            return null;
        }

        if (_schedules.TryGetValue(exchange, out (IReadOnlyList<TInvestTradingDay>? Days, DateTimeOffset ReadAt) cached) &&
            now - cached.ReadAt < (cached.Days is null ? FailureLifetime : Lifetime))
        {
            return cached.Days;
        }

        IReadOnlyList<TInvestTradingDay>? days = await Fetch(context, now, now.AddDays(7), cancellationToken);

        _schedules[exchange] = (days, now);

        return days;
    }

    /// <summary>
    ///     The trading days between two instants, read now and not kept, or null when the schedule cannot be read. It serves the
    ///     proof that the trading day of a stopped order has ended (ADR 0030), which needs days that are already past; a broker
    ///     that refuses such a range leaves the proof unmade.
    /// </summary>
    public Task<IReadOnlyList<TInvestTradingDay>?> DaysBetween(TInvestBotContext context, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        return string.IsNullOrEmpty(context.Share.Exchange) ? Task.FromResult<IReadOnlyList<TInvestTradingDay>?>(null) : Fetch(context, from, to, cancellationToken);
    }

    /// <summary>
    ///     The first trading day that has not ended yet: the current one during or between its sessions, else the next.
    /// </summary>
    public static TInvestTradingDay? Current(IReadOnlyList<TInvestTradingDay> days, DateTimeOffset now)
    {
        return days.FirstOrDefault(day => day.End > now);
    }

    private async Task<IReadOnlyList<TInvestTradingDay>?> Fetch(TInvestBotContext context, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        string exchange = context.Share.Exchange;

        try
        {
            TradingSchedulesResponse response = await transport.Unary(
                context.Token,
                TInvestMethodGroup.Instruments,
                (invoker, options) => new InstrumentsService.InstrumentsServiceClient(invoker).TradingSchedulesAsync(
                    new TradingSchedulesRequest { Exchange = exchange, From = from.ToTimestamp(), To = to.ToTimestamp() },
                    options),
                cancellationToken);

            return Classify(response.Exchanges.SingleOrDefault(schedule => string.Equals(schedule.Exchange, exchange, StringComparison.OrdinalIgnoreCase)));
        }
        catch (TInvestRpcException exception)
        {
            logger.LogWarning("The T-Invest trading schedule could not be read: {Failure}", exception.Kind);

            return null;
        }
    }

    private IReadOnlyList<TInvestTradingDay>? Classify(TradingSchedule? schedule)
    {
        if (schedule is null)
        {
            return null;
        }

        var days = new List<TInvestTradingDay>();

        foreach (TradingDay day in schedule.Days.OrderBy(day => day.Date.ToUtc()))
        {
            var sessions = new List<TInvestSessionInterval>();
            var known = true;
            List<(string Type, DateTimeOffset From, DateTimeOffset To)> intervals = day.Intervals.Where(interval => interval.Interval is not null)
                .Select(interval => (interval.Type, From: interval.Interval.StartTs.ToUtc(), To: interval.Interval.EndTs.ToUtc()))
                .ToList();

            foreach ((string type, DateTimeOffset from, DateTimeOffset to) in intervals)
            {
                if (TInvestScheduleVocabulary.IsExchange(type) || TInvestScheduleVocabulary.IsHoliday(type))
                {
                    sessions.Add(new TInvestSessionInterval(from, to, TInvestScheduleVocabulary.IsHoliday(type)));
                }
                else if (TInvestScheduleVocabulary.IsGeneric(type))
                {
                    known &= intervals.Any(other => other.From == from && other.To == to &&
                        (TInvestScheduleVocabulary.IsExchange(other.Type) || TInvestScheduleVocabulary.IsHoliday(other.Type)));
                }
                else if (!TInvestScheduleVocabulary.IsDealer(type))
                {
                    known = false;
                }
            }

            if (!known)
            {
                logger.LogWarning("The T-Invest trading schedule has an interval outside the vocabulary; that day plans no carry-over.");
            }

            days.Add(new TInvestTradingDay(DateOnly.FromDateTime(day.Date.ToDateTime()), [.. sessions.OrderBy(session => session.From)], known));
        }

        return days;
    }
}