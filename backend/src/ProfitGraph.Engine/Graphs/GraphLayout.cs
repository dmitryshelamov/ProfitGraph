using System.Collections.ObjectModel;

namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Снимок координат редактора; никогда не участвует в вычислении графа.
/// </summary>
public sealed record GraphLayout
{
    public IReadOnlyDictionary<string, NodePosition> Positions { get; }

    public GraphLayout(IReadOnlyDictionary<string, NodePosition> positions)
    {
        Positions = new ReadOnlyDictionary<string, NodePosition>(new Dictionary<string, NodePosition>(positions, StringComparer.Ordinal));
    }
}