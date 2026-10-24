using System.Collections.ObjectModel;

using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Выбранные значения Optimize для одного запуска, без перебора комбинаций.
/// </summary>
public sealed record GraphExecutionInputs
{
    public static GraphExecutionInputs Empty { get; } = new GraphExecutionInputs(new Dictionary<PortReference, double>());

    public IReadOnlyDictionary<PortReference, double> OptimizedValues { get; }

    public GraphExecutionInputs(IReadOnlyDictionary<PortReference, double> optimizedValues)
    {
        OptimizedValues = new ReadOnlyDictionary<PortReference, double>(new Dictionary<PortReference, double>(optimizedValues));
    }
}