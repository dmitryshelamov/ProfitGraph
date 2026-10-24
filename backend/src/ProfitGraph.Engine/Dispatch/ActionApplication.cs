namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Immediate action emissions and any external order outcome the dispatcher must correlate later.
/// </summary>
public sealed record ActionApplication(
    IReadOnlyList<LifecycleEmission> Emissions,
    long? PendingSubmitOrderId = null,
    long? PendingCancelOrderId = null);