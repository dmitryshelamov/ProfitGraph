namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The module's attempts table (<c>tinvest_order_attempts</c>, ADR 0023). Every write is one statement; a transition
///     applies only from the expected state, so concurrent callers cannot both move an attempt.
/// </summary>
internal interface ITInvestOrderAttempts
{
    /// <summary>
    ///     Records a new attempt; false when that attempt of the order already exists.
    /// </summary>
    Task<bool> Add(TInvestOrderAttempt attempt, CancellationToken cancellationToken);

    /// <summary>
    ///     The order's attempt with the highest number, or null.
    /// </summary>
    Task<TInvestOrderAttempt?> Latest(Guid botId, long engineOrderId, CancellationToken cancellationToken);

    Task<TInvestOrderAttempt?> ByRequestId(Guid requestId, CancellationToken cancellationToken);

    /// <summary>
    ///     Stores the attempt's new state, exchange order ID, broker code, price, waiting reason, cancel reason, carry-over date
    ///     and executed totals when it is still in
    ///     <paramref name="expected" />.
    /// </summary>
    Task<bool> Transition(TInvestOrderAttempt next, TInvestOrderAttemptState expected, CancellationToken cancellationToken);

    /// <summary>
    ///     Every attempt that waits locally, by bot and engine order.
    /// </summary>
    Task<IReadOnlyList<TInvestOrderAttempt>> Waiting(CancellationToken cancellationToken);

    /// <summary>
    ///     Every attempt that may be working at T-Invest and that the gateway may carry over.
    /// </summary>
    Task<IReadOnlyList<TInvestOrderAttempt>> Working(CancellationToken cancellationToken);

    /// <summary>
    ///     Every attempt of the engine order, oldest first.
    /// </summary>
    Task<IReadOnlyList<TInvestOrderAttempt>> ForOrder(Guid botId, long engineOrderId, CancellationToken cancellationToken);

    /// <summary>
    ///     The bot's attempts on the share that executed something and last changed at or after the time given, oldest first.
    /// </summary>
    Task<IReadOnlyList<TInvestOrderAttempt>> ExecutedOnShare(Guid botId, string instrumentUid, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    ///     Final attempts with executions whose commission T-Invest has not reported yet, changed since the time given.
    /// </summary>
    Task<IReadOnlyList<TInvestOrderAttempt>> PendingFees(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    ///     The bots that own the request IDs, among those the table knows.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Guid>> Owners(IReadOnlyCollection<Guid> requestIds, CancellationToken cancellationToken);
}