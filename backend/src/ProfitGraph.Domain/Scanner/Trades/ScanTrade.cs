using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Domain.Scanner.Trades;

/// <summary>
///     A trade the owner created from a scanner trade card (ADR 0060). Its own bot runs the level trade strategy with its prices and
///     amount; the trade follows the bot's position: it opens when the entry buys, and closes by the target, the stop or otherwise when
///     the position closes. An entry that waits past <see cref="ExpiresAt" /> is cancelled and the trade expires.
/// </summary>
public sealed class ScanTrade : AggregateRoot
{
    public ScanTradeId Id { get; }
    public UserId OwnerId { get; }
    public VenueId Venue { get; }
    public string Symbol { get; }
    public ScanScenario Scenario { get; }
    public decimal Entry { get; }
    public decimal Stop { get; }
    public decimal Target { get; }

    /// <summary>
    ///     The quote the entry spends.
    /// </summary>
    public decimal Amount { get; }

    /// <summary>
    ///     The quote the stop loses, fees included, as the amount was sized.
    /// </summary>
    public decimal Risk { get; }

    public BotId BotId { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public ScanTradeStatus Status { get; private set; }
    public DateTimeOffset? OpenedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>
    ///     Which of the strategy's sales left the position, once the venue filled one; null before.
    /// </summary>
    public ScanTradeExit? Exit { get; private set; }

    /// <summary>
    ///     The bot's realized profit once the position closed, after fees.
    /// </summary>
    public decimal? RealizedPnl { get; private set; }

    /// <summary>
    ///     Why the trade failed, as a stable code; null unless it did.
    /// </summary>
    public string? FailureCode { get; private set; }

    /// <summary>
    ///     Whether a finished trade's bot was stopped or found stopped, so the Worker no longer looks at it.
    /// </summary>
    public bool BotReleased { get; private set; }

    public bool IsLive => IsLiveStatus(Status);

    private ScanTrade(
        ScanTradeId id,
        UserId ownerId,
        VenueId venue,
        string symbol,
        ScanScenario scenario,
        decimal entry,
        decimal stop,
        decimal target,
        decimal amount,
        decimal risk,
        BotId botId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        ScanTradeStatus status,
        DateTimeOffset? openedAt,
        DateTimeOffset? closedAt,
        ScanTradeExit? exit,
        decimal? realizedPnl,
        string? failureCode,
        bool botReleased)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (stop <= 0m || stop >= entry || target <= entry)
        {
            throw new ScanTradeRuleException(ScanTradeRuleException.InvalidLevels, "A trade's stop is positive and below its entry, and its target above it.");
        }

        if (amount <= 0m || risk <= 0m || expiresAt <= createdAt || !Enum.IsDefined(scenario) || !Enum.IsDefined(status) || (botReleased && IsLiveStatus(status)))
        {
            throw new ArgumentException("A trade spends and risks a positive amount and its entry expires after it was created.");
        }

        Id = id;
        OwnerId = ownerId;
        Venue = venue;
        Symbol = symbol;
        Scenario = scenario;
        Entry = entry;
        Stop = stop;
        Target = target;
        Amount = amount;
        Risk = risk;
        BotId = botId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Status = status;
        OpenedAt = openedAt;
        ClosedAt = closedAt;
        Exit = exit;
        RealizedPnl = realizedPnl;
        FailureCode = failureCode;
        BotReleased = botReleased;
    }

    public static ScanTrade Create(
        ScanTradeId id,
        UserId ownerId,
        VenueId venue,
        string symbol,
        ScanScenario scenario,
        decimal entry,
        decimal stop,
        decimal target,
        decimal amount,
        decimal risk,
        BotId botId,
        TimeSpan entryValidity,
        DateTimeOffset now)
    {
        return new ScanTrade(
            id,
            ownerId,
            venue,
            symbol,
            scenario,
            entry,
            stop,
            target,
            amount,
            risk,
            botId,
            now,
            now + entryValidity,
            ScanTradeStatus.Waiting,
            openedAt: null,
            closedAt: null,
            exit: null,
            realizedPnl: null,
            failureCode: null,
            botReleased: false);
    }

    public static ScanTrade Restore(
        ScanTradeId id,
        UserId ownerId,
        VenueId venue,
        string symbol,
        ScanScenario scenario,
        decimal entry,
        decimal stop,
        decimal target,
        decimal amount,
        decimal risk,
        BotId botId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        ScanTradeStatus status,
        DateTimeOffset? openedAt,
        DateTimeOffset? closedAt,
        ScanTradeExit? exit,
        decimal? realizedPnl,
        string? failureCode,
        bool botReleased)
    {
        return new ScanTrade(
            id,
            ownerId,
            venue,
            symbol,
            scenario,
            entry,
            stop,
            target,
            amount,
            risk,
            botId,
            createdAt,
            expiresAt,
            status,
            openedAt,
            closedAt,
            exit,
            realizedPnl,
            failureCode,
            botReleased);
    }

    /// <summary>
    ///     Whether the entry has waited too long: the trade still waits and <see cref="ExpiresAt" /> has passed.
    /// </summary>
    public bool IsOverdue(DateTimeOffset now)
    {
        return Status == ScanTradeStatus.Waiting && now >= ExpiresAt;
    }

    /// <summary>
    ///     The bot's position opened: the entry bought.
    /// </summary>
    public bool Open(DateTimeOffset at)
    {
        if (Status != ScanTradeStatus.Waiting)
        {
            return false;
        }

        Status = ScanTradeStatus.Open;
        OpenedAt = at;

        return true;
    }

    /// <summary>
    ///     The venue filled one of the strategy's sales; a trade already closed takes its exit as its status.
    /// </summary>
    public bool RecordExit(ScanTradeExit exit)
    {
        if (Exit is not null || exit == ScanTradeExit.Time)
        {
            return false;
        }

        Exit = exit;

        if (Status == ScanTradeStatus.Closed)
        {
            Status = Closed(exit);
        }

        return true;
    }

    /// <summary>
    ///     The bot's position closed with <paramref name="realizedPnl" />.
    /// </summary>
    public bool Close(decimal realizedPnl, DateTimeOffset at)
    {
        if (Status is not (ScanTradeStatus.Waiting or ScanTradeStatus.Open))
        {
            return false;
        }

        Status = Exit is { } exit ? Closed(exit) : ScanTradeStatus.Closed;
        OpenedAt ??= at;
        ClosedAt = at;
        RealizedPnl = realizedPnl;

        return true;
    }

    /// <summary>
    ///     The entry was cancelled after <see cref="ExpiresAt" /> without buying.
    /// </summary>
    public bool Expire(DateTimeOffset at)
    {
        return End(ScanTradeStatus.Expired, failureCode: null, at);
    }

    /// <summary>
    ///     The bot stopped before the entry bought.
    /// </summary>
    public bool Cancel(DateTimeOffset at)
    {
        return End(ScanTradeStatus.Cancelled, failureCode: null, at);
    }

    /// <summary>
    ///     The bot could not start, or the entry ended without buying before it expired.
    /// </summary>
    public bool Fail(string failureCode, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);

        return End(ScanTradeStatus.Failed, failureCode, at);
    }

    /// <summary>
    ///     The finished trade's bot stopped; returns whether that changed anything.
    /// </summary>
    public bool ReleaseBot()
    {
        if (IsLive || BotReleased)
        {
            return false;
        }

        BotReleased = true;

        return true;
    }

    private bool End(ScanTradeStatus status, string? failureCode, DateTimeOffset at)
    {
        if (Status != ScanTradeStatus.Waiting)
        {
            return false;
        }

        Status = status;
        FailureCode = failureCode;
        ClosedAt = at;

        return true;
    }

    private static bool IsLiveStatus(ScanTradeStatus status)
    {
        return status is ScanTradeStatus.Waiting or ScanTradeStatus.Open;
    }

    private static ScanTradeStatus Closed(ScanTradeExit exit)
    {
        return exit == ScanTradeExit.Target ? ScanTradeStatus.TookProfit : ScanTradeStatus.StoppedOut;
    }
}