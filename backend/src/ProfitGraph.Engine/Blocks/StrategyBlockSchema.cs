using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Blocks;

public static class StrategyBlockSchema
{
    public static IReadOnlyList<string> Sides { get; } = Array.AsReadOnly(new[] { "Buy", "Sell" });
    public static IReadOnlyList<string> SizeModes { get; } = Array.AsReadOnly(new[] { "amount", "quantity" });
    public static IReadOnlyList<string> OrderKinds { get; } = Array.AsReadOnly(new[] { "Market", "Limit" });

    /// <summary>
    ///     The size an order node of the side takes: a long's buy spends a quote amount, every other order gives a base quantity, a
    ///     short's buy included (ADR 0061).
    /// </summary>
    public static string NativeSize(string? side, PositionDirection direction)
    {
        return side == "Buy" && direction == PositionDirection.Long ? "amount" : "quantity";
    }

    public static IReadOnlyList<string> CheckTypes { get; } = Array.AsReadOnly(
        new[]
        {
            "Compare", "CrossesAbove", "CrossesBelow", "PositionFlat", "PositionFlatPreview", "NoActiveOrders", "NoActiveTaggedOrder", "HasBaseBalance",
            "OrderSizePreviewValid"
        });
}