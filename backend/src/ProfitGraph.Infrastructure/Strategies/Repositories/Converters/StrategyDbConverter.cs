using ProfitGraph.Domain.Strategies;
using ProfitGraph.Infrastructure.Strategies.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Strategies.Repositories.Converters;

internal static class StrategyDbConverter
{
    public static Strategy ToDomain(this StrategyDb row)
    {
        return Strategy.Restore(row.Id, row.OwnerId, row.Name, row.CurrentVersion, row.CreatedAt, row.UpdatedAt);
    }
}