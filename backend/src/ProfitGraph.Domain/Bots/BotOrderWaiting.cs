using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     A bot's working order that its venue holds locally (ADR 0023, TC4): why, since when, until when if known, and the
///     price bounds that keep it when a price band holds it. A change of reason is reported; the same reason with new
///     details is not.
/// </summary>
public sealed class BotOrderWaiting : AggregateRoot
{
    public BotId BotId { get; }
    public long EngineOrderId { get; }
    public OrderWaitingCode Code { get; private set; }
    public DateTimeOffset Since { get; private set; }
    public DateTimeOffset? Until { get; private set; }
    public decimal? Low { get; private set; }
    public decimal? High { get; private set; }

    private BotOrderWaiting(BotId botId, long engineOrderId, OrderWaitingCode code, DateTimeOffset since, DateTimeOffset? until, decimal? low, decimal? high)
    {
        BotId = botId;
        EngineOrderId = engineOrderId;
        Set(code, since, until, low, high);
    }

    public static BotOrderWaiting Start(BotId botId, long engineOrderId, OrderWaitingCode code, DateTimeOffset now, DateTimeOffset? until, decimal? low, decimal? high)
    {
        var waiting = new BotOrderWaiting(botId, engineOrderId, code, now, until, low, high);
        waiting.AddDomainEvent(new BotOrderWaitingStarted(botId, engineOrderId, code, until, low, high, now));

        return waiting;
    }

    public static BotOrderWaiting Restore(
        BotId botId,
        long engineOrderId,
        OrderWaitingCode code,
        DateTimeOffset since,
        DateTimeOffset? until,
        decimal? low,
        decimal? high)
    {
        return new BotOrderWaiting(botId, engineOrderId, code, since, until, low, high);
    }

    /// <summary>
    ///     Records the current reason; returns whether anything changed. A new reason starts a new wait and is reported.
    /// </summary>
    public bool Change(OrderWaitingCode code, DateTimeOffset now, DateTimeOffset? until, decimal? low, decimal? high)
    {
        if (code == Code && until == Until && low == Low && high == High)
        {
            return false;
        }

        if (code != Code)
        {
            AddDomainEvent(new BotOrderWaitingStarted(BotId, EngineOrderId, code, until, low, high, now));
            Since = now;
        }

        Set(code, Since, until, low, high);

        return true;
    }

    /// <summary>
    ///     The venue placed the order or the order ended; the wait is over and is reported once.
    /// </summary>
    public void End(DateTimeOffset now)
    {
        AddDomainEvent(new BotOrderWaitingEnded(BotId, EngineOrderId, Code, now));
    }

    private void Set(OrderWaitingCode code, DateTimeOffset since, DateTimeOffset? until, decimal? low, decimal? high)
    {
        if (!Enum.IsDefined(code) || low <= 0m || high <= 0m || (low is { } bottom && high is { } top && bottom > top) || until < since)
        {
            throw new ArgumentException("A waiting order has a known reason, positive ordered bounds and an end after its start.");
        }

        Code = code;
        Since = since;
        Until = until;
        Low = low;
        High = high;
    }
}