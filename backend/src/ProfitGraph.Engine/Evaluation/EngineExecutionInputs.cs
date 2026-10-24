using System.Collections.ObjectModel;

using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Immutable exact numeric optimization choices for one engine execution.
/// </summary>
public sealed class EngineExecutionInputs
{
    public static EngineExecutionInputs Empty { get; } = new EngineExecutionInputs(new Dictionary<PortReference, decimal>());

    public IReadOnlyDictionary<PortReference, decimal> OptimizedValues { get; }

    public EngineExecutionInputs(IReadOnlyDictionary<PortReference, decimal> optimizedValues)
    {
        OptimizedValues = new ReadOnlyDictionary<PortReference, decimal>(new Dictionary<PortReference, decimal>(optimizedValues));
    }
}