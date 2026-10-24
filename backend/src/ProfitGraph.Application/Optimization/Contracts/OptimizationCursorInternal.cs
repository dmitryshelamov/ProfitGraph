namespace ProfitGraph.Application.Optimization.Contracts;

/// <summary>
///     Where an optimization's progress stands when a client subscribes: its last stored trial and its job's status.
/// </summary>
public sealed record OptimizationCursorInternal(long LastSequence, string Status)
{
    public bool IsFinished => Status is "Completed" or "Failed" or "Cancelled";
}