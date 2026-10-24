using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.Converters;

/// <summary>
///     Преобразует стратегию и её текущий снимок во внутренний контракт Application.
/// </summary>
public static class StrategyInternalConverter
{
    public static StrategyInternal ToInternal(this Strategy strategy, StrategyVersion version)
    {
        return new StrategyInternal(
            strategy.Id.Value,
            strategy.Name.Value,
            strategy.CurrentVersion.Value,
            strategy.CreatedAt,
            strategy.UpdatedAt,
            version.Definition.ToInternal(),
            version.Layout.ToInternal());
    }
}