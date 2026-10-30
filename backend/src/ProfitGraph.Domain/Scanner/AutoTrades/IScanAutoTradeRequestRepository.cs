namespace ProfitGraph.Domain.Scanner.AutoTrades;

public interface IScanAutoTradeRequestRepository
{
    Task Add(ScanAutoTradeRequest request, CancellationToken cancellationToken);

    /// <summary>
    ///     The oldest pending requests no other Worker holds, and those claimed by a Worker that did not finish them before
    ///     <paramref name="staleBefore" />, locked until the transaction ends.
    /// </summary>
    Task<IReadOnlyList<ScanAutoTradeRequest>> ClaimPending(int limit, DateTimeOffset staleBefore, CancellationToken cancellationToken);

    Task<ScanAutoTradeRequest?> GetForUpdate(ScanAutoTradeRequestId id, CancellationToken cancellationToken);

    Task Update(ScanAutoTradeRequest request, CancellationToken cancellationToken);

    /// <summary>
    ///     The rule's latest requests, newest first.
    /// </summary>
    Task<IReadOnlyList<ScanAutoTradeRequest>> List(ScanAutoTradeRuleId ruleId, int limit, CancellationToken cancellationToken);

    /// <summary>
    ///     Deletes the requests created before <paramref name="cutoff" />.
    /// </summary>
    Task DeleteBefore(DateTimeOffset cutoff, CancellationToken cancellationToken);
}