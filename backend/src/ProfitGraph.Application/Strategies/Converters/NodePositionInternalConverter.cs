using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Application.Strategies.Converters;

/// <summary>
///     Преобразует координаты ноды между Domain и внутренним контрактом Application.
/// </summary>
public static class NodePositionInternalConverter
{
    public static NodePosition ToDomain(this NodePositionInternal position)
    {
        return new NodePosition(position.X, position.Y);
    }

    public static NodePositionInternal ToInternal(this NodePosition position)
    {
        return new NodePositionInternal(position.X, position.Y);
    }
}