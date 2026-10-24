using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Infrastructure.Strategies.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Strategies.Repositories.Converters;

internal static class StrategyDbConverter
{
    public static Strategy ToDomain(this StrategyDb row)
    {
        return Strategy.Restore(
            new StrategyId(row.Id),
            new UserId(row.OwnerId),
            new StrategyName(row.Name),
            new StrategyVersionNumber(row.CurrentVersion),
            row.CreatedAt,
            row.UpdatedAt);
    }

    public static StrategyDb ToDb(this Strategy strategy)
    {
        return new StrategyDb
        {
            Id = strategy.Id.Value,
            OwnerId = strategy.OwnerId.Value,
            Name = strategy.Name.Value,
            CurrentVersion = strategy.CurrentVersion.Value,
            CreatedAt = strategy.CreatedAt,
            UpdatedAt = strategy.UpdatedAt
        };
    }
}