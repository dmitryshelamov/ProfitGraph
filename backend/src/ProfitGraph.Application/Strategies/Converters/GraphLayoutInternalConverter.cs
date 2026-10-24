using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Application.Strategies.Converters;

/// <summary>
///     Преобразует расположение нод между Domain и внутренним контрактом Application.
/// </summary>
public static class GraphLayoutInternalConverter
{
    public static GraphLayout ToDomain(this GraphLayoutInternal layout)
    {
        return new GraphLayout(layout.Positions.ToDictionary(pair => pair.Key, pair => pair.Value.ToDomain(), StringComparer.Ordinal));
    }

    public static GraphLayoutInternal ToInternal(this GraphLayout layout)
    {
        return new GraphLayoutInternal(layout.Positions.ToDictionary(pair => pair.Key, pair => pair.Value.ToInternal()));
    }
}