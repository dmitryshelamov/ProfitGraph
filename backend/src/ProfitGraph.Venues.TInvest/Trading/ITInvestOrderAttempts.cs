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
    ///     Stores the attempt's new state, exchange order ID and broker code when it is still in <paramref name="expected" />.
    /// </summary>
    Task<bool> Transition(TInvestOrderAttempt next, TInvestOrderAttemptState expected, CancellationToken cancellationToken);

    /// <summary>
    ///     The bots that own the request IDs, among those the table knows.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Guid>> Owners(IReadOnlyCollection<Guid> requestIds, CancellationToken cancellationToken);
}