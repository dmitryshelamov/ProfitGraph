using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The totals of an order the venue executed completely (ADR 0042), as the bot's fills record them: gross base and quote,
///     the fee and its asset, the realized profit of a sell, and the instrument's quantity step, price tick and quote
///     precision to show them with.
/// </summary>
public sealed record BotOrderFill
{
    public long EngineOrderId { get; }
    public OrderSide Side { get; }
    public string? Tag { get; }
    public decimal GrossBase { get; }
    public decimal GrossQuote { get; }
    public decimal Fee { get; }
    public FeeAsset FeeAsset { get; }

    /// <summary>
    ///     The profit the order realized, over all its fills; null for a buy.
    /// </summary>
    public decimal? RealizedPnl { get; }

    public decimal QuantityStep { get; }
    public decimal PriceTick { get; }
    public int QuotePrecision { get; }

    public decimal AveragePrice => GrossQuote / GrossBase;

    public BotOrderFill(
        long engineOrderId,
        OrderSide side,
        string? tag,
        decimal grossBase,
        decimal grossQuote,
        decimal fee,
        FeeAsset feeAsset,
        decimal? realizedPnl,
        decimal quantityStep,
        decimal priceTick,
        int quotePrecision)
    {
        if (grossBase <= 0m || grossQuote <= 0m || quantityStep <= 0m || priceTick <= 0m || quotePrecision < 0 || side == OrderSide.Buy == realizedPnl.HasValue)
        {
            throw new ArgumentException("A filled order has positive amounts and steps, and a realized profit exactly when it sold.");
        }

        EngineOrderId = engineOrderId;
        Side = side;
        Tag = tag;
        GrossBase = grossBase;
        GrossQuote = grossQuote;
        Fee = fee;
        FeeAsset = feeAsset;
        RealizedPnl = realizedPnl;
        QuantityStep = quantityStep;
        PriceTick = priceTick;
        QuotePrecision = quotePrecision;
    }
}