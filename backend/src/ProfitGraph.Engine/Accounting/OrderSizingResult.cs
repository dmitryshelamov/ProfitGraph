namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Pure normalized sizing result; a market quantity is absent until a reference execution price is supplied.
/// </summary>
public readonly record struct OrderSizingResult(decimal? Quantity, decimal? Price, decimal Reservation, RejectionReason? Reason)
{
    public decimal QuoteFee { get; init; }

    public bool IsValid => Reason is null;
}