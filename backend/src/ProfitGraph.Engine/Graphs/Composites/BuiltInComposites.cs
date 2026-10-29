namespace ProfitGraph.Engine.Graphs.Composites;

public static class BuiltInComposites
{
    public static IReadOnlyList<CompositeNodeDefinition> Definitions { get; } =
    [
        PositionFlatCompositeDefinition.Create(),
        NoActiveOrdersCompositeDefinition.Create(),
        NoActiveTaggedOrderCompositeDefinition.Create(),
        HasBaseBalanceCompositeDefinition.Create(),
        PositionFlatPreviewCompositeDefinition.Create(),
        BollingerBandsCompositeDefinition.Create(),
        MacdCompositeDefinition.Create()
    ];
}