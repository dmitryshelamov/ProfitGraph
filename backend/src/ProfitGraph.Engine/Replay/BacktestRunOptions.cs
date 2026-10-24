using ProfitGraph.Engine.Dispatch;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Controls result retention independently of observer delivery.
/// </summary>
public sealed record BacktestRunOptions
{
    internal bool RetainDispatchProjections { get; init; }
    internal Action<DispatchBatch>? DispatchObserver { get; init; }

    public bool RetainLedger { get; init; } = true;
}