using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     История выходов только одного запуска; не хранится в определениях нод.
/// </summary>
public sealed class GraphExecutionState
{
    private readonly Dictionary<PortReference, List<NodeOutput>> _history = [];

    public IReadOnlyList<NodeOutput> History(PortReference port)
    {
        return _history.TryGetValue(port, out List<NodeOutput>? values) ? values.AsReadOnly() : Array.Empty<NodeOutput>();
    }

    internal void Append(PortReference port, NodeOutput value)
    {
        if (!_history.TryGetValue(port, out List<NodeOutput>? values))
        {
            values = [];
            _history.Add(port, values);
        }

        values.Add(value);
    }
}