using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.Converters;

/// <summary>
///     Преобразует снимок версии стратегии во внутренний контракт Application.
/// </summary>
public static class StrategyVersionInternalConverter
{
    public static StrategyVersionInternal ToInternal(this StrategyVersion version)
    {
        return new StrategyVersionInternal(
            version.Id,
            version.StrategyId,
            version.Number,
            version.CreatedAt,
            version.Definition.ToInternal(),
            version.Layout.ToInternal());
    }
}