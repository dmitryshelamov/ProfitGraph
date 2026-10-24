using ProfitGraph.Api.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Api.Strategies.Converters;

internal static class BlockCatalogResponseConverter
{
    public static BlockCatalogResponse ToResponse(this BlockCatalogInternal catalog)
    {
        return new BlockCatalogResponse(
            Map(catalog.BlockKinds),
            Map(catalog.EventSources),
            Map(catalog.CheckTypes),
            Map(catalog.OrderKinds),
            Map(catalog.ValueSources));
    }

    private static BlockCatalogEntryResponse[] Map(IReadOnlyList<BlockCatalogEntryInternal> entries)
    {
        return
        [
            .. entries.Select(entry => new BlockCatalogEntryResponse(
                entry.Key,
                entry.Label,
                [.. entry.Operands.Select(Map)],
                entry.OutputType,
                entry.Choices,
                entry.Fields?.Select(Map).ToArray(),
                entry.Sides,
                entry.SizeModes,
                entry.OrderKinds,
                entry.MinimumOrderPolicies))
        ];
    }

    private static BlockCatalogOperandResponse Map(BlockCatalogOperandInternal operand)
    {
        return new BlockCatalogOperandResponse(operand.Key, operand.Label, operand.ValueTypes, operand.Required);
    }
}