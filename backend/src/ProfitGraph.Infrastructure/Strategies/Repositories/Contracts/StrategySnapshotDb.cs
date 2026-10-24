using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Infrastructure.Strategies.Repositories.Contracts;

internal sealed record StrategySnapshotDb(int FormatVersion, StrategyDefinitionInternal Definition, GraphLayoutInternal Layout);