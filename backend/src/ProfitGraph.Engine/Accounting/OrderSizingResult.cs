namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Pure normalized sizing result; a market quantity is absent until a reference execution price is supplied.
/// </summary>
public readonly record struct OrderSizingResult(decimal? Quantity, decimal? Price, decimal Reservation, RejectionReason? Reason)
{
    public decimal QuoteFee { get; init; }

    /// <summary>
    ///     A margin run's market buy spends up to this quote while it reserves only its margin (ADR 0061); null otherwise.
    /// </summary>
    public decimal? QuoteBudget { get; init; }

    public bool IsValid => Reason is null;
}