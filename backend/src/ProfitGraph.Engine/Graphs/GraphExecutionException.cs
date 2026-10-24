using ProfitGraph.Engine.Common;

namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Диагностика вычисления с координатами ноды и бара; не является результатом торговли.
/// </summary>
public sealed class GraphExecutionException(string nodeId, int barIndex, string message, Exception? innerException = null, long? occurrenceId = null)
    : EngineException($"Node '{nodeId}' failed at bar {barIndex}: {message}", innerException)
{
    public string NodeId { get; } = nodeId;
    public int BarIndex { get; } = barIndex;
    public long? OccurrenceId { get; } = occurrenceId;
}