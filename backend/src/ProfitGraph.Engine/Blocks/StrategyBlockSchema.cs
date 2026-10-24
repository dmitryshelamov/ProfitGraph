namespace ProfitGraph.Engine.Blocks;

public static class StrategyBlockSchema
{
    public static IReadOnlyList<string> Sides { get; } = Array.AsReadOnly(new[] { "Buy", "Sell" });
    public static IReadOnlyList<string> SizeModes { get; } = Array.AsReadOnly(new[] { "amount", "quantity" });
    public static IReadOnlyList<string> OrderKinds { get; } = Array.AsReadOnly(new[] { "Market", "Limit" });

    public static IReadOnlyList<string> CheckTypes { get; } = Array.AsReadOnly(
        new[]
        {
            "Compare", "CrossesAbove", "CrossesBelow", "PositionFlat", "PositionFlatPreview", "NoActiveOrders", "NoActiveTaggedOrder", "HasBaseBalance",
            "OrderSizePreviewValid"
        });
}