namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record BlockCatalogInternal(
    IReadOnlyList<BlockCatalogEntryInternal> BlockKinds,
    IReadOnlyList<BlockCatalogEntryInternal> EventSources,
    IReadOnlyList<BlockCatalogEntryInternal> CheckTypes,
    IReadOnlyList<BlockCatalogEntryInternal> OrderKinds,
    IReadOnlyList<BlockCatalogEntryInternal> ValueSources);