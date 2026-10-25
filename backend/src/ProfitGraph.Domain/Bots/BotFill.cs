using ProfitGraph.Domain.Common;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     One execution of a bot's order as the engine accounted it (ADR 0021): the change since the order's previous fill.
///     The order's cumulative gross base after it identifies it, so the same venue state is recorded once.
/// </summary>
public sealed class BotFill : AggregateRoot
{
    public BotFillId Id { get; }
    public BotId BotId { get; }
    public long EngineOrderId { get; }
    public OrderSide Side { get; }
    public string? Tag { get; }
    public decimal GrossBase { get; }
    public decimal GrossQuote { get; }
    public decimal Fee { get; }

    /// <summary>
    ///     The asset the fee was paid in: a sell pays in quote; a buy pays in base (Profit Fox Designer ADR 0055) or in quote
    ///     where the instrument's rules charge fees in quote (ADR 0023).
    /// </summary>
    public FeeAsset FeeAsset { get; }

    public decimal CumulativeGrossBase { get; }
    public DateTimeOffset FilledAt { get; }

    public decimal Price => GrossQuote / GrossBase;

    private BotFill(
        BotFillId id,
        BotId botId,
        long engineOrderId,
        OrderSide side,
        string? tag,
        decimal grossBase,
        decimal grossQuote,
        decimal fee,
        decimal cumulativeGrossBase,
        DateTimeOffset filledAt,
        FeeAsset? feeAsset)
    {
        if (grossBase <= 0m || grossQuote <= 0m || fee < 0m || cumulativeGrossBase < grossBase)
        {
            throw new ArgumentException("A fill has positive amounts within its order's cumulative execution.");
        }

        Id = id;
        BotId = botId;
        EngineOrderId = engineOrderId;
        Side = side;
        Tag = tag;
        GrossBase = grossBase;
        GrossQuote = grossQuote;
        Fee = fee;
        CumulativeGrossBase = cumulativeGrossBase;
        FilledAt = filledAt;
        FeeAsset = feeAsset ?? (side == OrderSide.Buy ? FeeAsset.Base : FeeAsset.Quote);
    }

    public static BotFill Record(
        BotFillId id,
        BotId botId,
        long engineOrderId,
        OrderSide side,
        string? tag,
        decimal grossBase,
        decimal grossQuote,
        decimal fee,
        decimal cumulativeGrossBase,
        DateTimeOffset filledAt,
        FeeAsset? feeAsset = null)
    {
        var fill = new BotFill(id, botId, engineOrderId, side, tag, grossBase, grossQuote, fee, cumulativeGrossBase, filledAt, feeAsset);
        fill.AddDomainEvent(new BotFillRecorded(botId, id, engineOrderId, side, grossBase, grossQuote, filledAt));

        return fill;
    }

    public static BotFill Restore(
        BotFillId id,
        BotId botId,
        long engineOrderId,
        OrderSide side,
        string? tag,
        decimal grossBase,
        decimal grossQuote,
        decimal fee,
        decimal cumulativeGrossBase,
        DateTimeOffset filledAt,
        FeeAsset? feeAsset = null)
    {
        return new BotFill(id, botId, engineOrderId, side, tag, grossBase, grossQuote, fee, cumulativeGrossBase, filledAt, feeAsset);
    }
}