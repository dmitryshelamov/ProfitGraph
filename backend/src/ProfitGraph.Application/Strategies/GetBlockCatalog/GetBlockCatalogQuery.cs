using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.GetBlockCatalog;

public sealed record GetBlockCatalogQuery : IQuery<BlockCatalogInternal>;