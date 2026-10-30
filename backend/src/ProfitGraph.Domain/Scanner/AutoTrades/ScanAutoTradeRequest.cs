using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     A pair that entered an auto trade rule's preset (ADR 0071, section 6): the snapshot that found it and its price there. The
///     Worker handles it once: into a trade, a paper trade or a skip with its reason, so the page shows why a signal did not trade.
/// </summary>
public sealed class ScanAutoTradeRequest : AggregateRoot
{
    public ScanAutoTradeRequestId Id { get; }
    public ScanAutoTradeRuleId RuleId { get; }
    public string Symbol { get; }
    public DateTimeOffset SnapshotAt { get; }
    public decimal? Price { get; }
    public DateTimeOffset CreatedAt { get; }
    public ScanAutoTradeRequestStatus Status { get; private set; }
    public ScanAutoTradeSkip? Skip { get; private set; }

    /// <summary>
    ///     The error a refused trade answered with.
    /// </summary>
    public string? ErrorCode { get; private set; }

    /// <summary>
    ///     The paper trade the request became.
    /// </summary>
    public ScanPaperTradeId? PaperTradeId { get; private set; }

    public DateTimeOffset? HandledAt { get; private set; }

    private ScanAutoTradeRequest(
        ScanAutoTradeRequestId id,
        ScanAutoTradeRuleId ruleId,
        string symbol,
        DateTimeOffset snapshotAt,
        decimal? price,
        DateTimeOffset createdAt,
        ScanAutoTradeRequestStatus status,
        ScanAutoTradeSkip? skip,
        string? errorCode,
        ScanPaperTradeId? paperTradeId,
        DateTimeOffset? handledAt)
    {
        Id = id;
        RuleId = ruleId;
        Symbol = symbol;
        SnapshotAt = snapshotAt;
        Price = price;
        CreatedAt = createdAt;
        Status = status;
        Skip = skip;
        ErrorCode = errorCode;
        PaperTradeId = paperTradeId;
        HandledAt = handledAt;
    }

    public static ScanAutoTradeRequest Create(ScanAutoTradeRuleId ruleId, string symbol, DateTimeOffset snapshotAt, decimal? price, DateTimeOffset now)
    {
        return new ScanAutoTradeRequest(
            ScanAutoTradeRequestId.New(),
            ruleId,
            symbol,
            snapshotAt,
            price,
            now,
            ScanAutoTradeRequestStatus.Pending,
            skip: null,
            errorCode: null,
            paperTradeId: null,
            handledAt: null);
    }

    public static ScanAutoTradeRequest Restore(
        ScanAutoTradeRequestId id,
        ScanAutoTradeRuleId ruleId,
        string symbol,
        DateTimeOffset snapshotAt,
        decimal? price,
        DateTimeOffset createdAt,
        ScanAutoTradeRequestStatus status,
        ScanAutoTradeSkip? skip,
        string? errorCode,
        ScanPaperTradeId? paperTradeId,
        DateTimeOffset? handledAt)
    {
        return new ScanAutoTradeRequest(id, ruleId, symbol, snapshotAt, price, createdAt, status, skip, errorCode, paperTradeId, handledAt);
    }

    /// <summary>
    ///     The Worker reads the pair's cards outside a transaction; a claimed request is finished afterwards.
    /// </summary>
    public void Claim()
    {
        if (Status != ScanAutoTradeRequestStatus.Pending)
        {
            throw new ScanAutoTradeRuleException(ScanAutoTradeRuleException.WrongOrder, "Only a pending auto trade request is claimed.");
        }

        Status = ScanAutoTradeRequestStatus.Handling;
    }

    public void TookOnPaper(ScanPaperTradeId paperTradeId, DateTimeOffset now)
    {
        Handle(ScanAutoTradeRequestStatus.Paper, skip: null, errorCode: null, paperTradeId, now);
    }

    public void Skipped(ScanAutoTradeSkip skip, DateTimeOffset now, string? errorCode = null)
    {
        Handle(ScanAutoTradeRequestStatus.Skipped, skip, errorCode, paperTradeId: null, now);
    }

    private void Handle(ScanAutoTradeRequestStatus status, ScanAutoTradeSkip? skip, string? errorCode, ScanPaperTradeId? paperTradeId, DateTimeOffset now)
    {
        if (Status is not (ScanAutoTradeRequestStatus.Pending or ScanAutoTradeRequestStatus.Handling))
        {
            throw new ScanAutoTradeRuleException(ScanAutoTradeRuleException.WrongOrder, "An auto trade request is handled once.");
        }

        Status = status;
        Skip = skip;
        ErrorCode = errorCode;
        PaperTradeId = paperTradeId;
        HandledAt = now;
    }
}