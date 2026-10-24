namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record BlockCatalogResponse(
    IReadOnlyList<BlockCatalogEntryResponse> BlockKinds,
    IReadOnlyList<BlockCatalogEntryResponse> EventSources,
    IReadOnlyList<BlockCatalogEntryResponse> CheckTypes,
    IReadOnlyList<BlockCatalogEntryResponse> OrderKinds,
    IReadOnlyList<BlockCatalogEntryResponse> ValueSources);