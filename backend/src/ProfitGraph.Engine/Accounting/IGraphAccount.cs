namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Committed account state and synchronous order actions required by graph dispatch.
/// </summary>
public interface IGraphAccount
{
    public ExecutionSettings Settings { get; }
    public InstrumentRules Rules { get; }

    public AccountSnapshot Capture();
    public OrderSubmission Submit(OrderRequest request);

    /// <summary>
    ///     Cancels an active order, or returns null for a successful no-op when no active order exists.
    /// </summary>
    public Order? Cancel(long orderId);

    /// <summary>
    ///     Cancels the active order with this tag, or returns null for a successful no-op when none exists.
    /// </summary>
    public Order? CancelByTag(string tag);

    /// <summary>
    ///     What the order has executed so far, also after it ended in this process, or none.
    /// </summary>
    public OrderExecutedTotals ExecutedTotals(long orderId)
    {
        return OrderExecutedTotals.None;
    }
}