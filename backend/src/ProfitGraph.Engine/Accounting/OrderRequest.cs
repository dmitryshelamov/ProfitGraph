namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Immutable graph order request. Buy Amount is a quote budget; sell Amount is base quantity.
///     The optional sizing policy is internal request behavior, separate from the existing public observation shape.
/// </summary>
public sealed record OrderRequest
{
    private readonly MinimumOrderPolicy _minimumOrderPolicy;
    private readonly decimal? _referencePrice;

    public OrderSide Side { get; init; }
    public OrderType Type { get; init; }
    public decimal Amount { get; init; }
    public decimal? LimitPrice { get; init; }
    public string? Tag { get; init; }
    public string OriginNodeId { get; init; }
    public int EligibleFromCandleIndex { get; init; }

    public OrderRequest(
        OrderSide Side,
        OrderType Type,
        decimal Amount,
        decimal? LimitPrice,
        string? Tag,
        string OriginNodeId,
        int EligibleFromCandleIndex,
        MinimumOrderPolicy MinimumOrderPolicy = MinimumOrderPolicy.Reject,
        decimal? ReferencePrice = null)
    {
        this.Side = Side;
        this.Type = Type;
        this.Amount = Amount;
        this.LimitPrice = LimitPrice;
        this.Tag = Tag;
        this.OriginNodeId = OriginNodeId;
        this.EligibleFromCandleIndex = EligibleFromCandleIndex;
        _minimumOrderPolicy = MinimumOrderPolicy;
        _referencePrice = ReferencePrice;
    }

    public decimal? GetReferencePrice()
    {
        return _referencePrice;
    }

    public MinimumOrderPolicy GetMinimumOrderPolicy()
    {
        return _minimumOrderPolicy;
    }
}