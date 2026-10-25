namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Who asked T-Invest to cancel an attempt, recorded before the request leaves (ADR 0023, T12b). An owner or strategy
///     cancel ends the engine order; a carry-over keeps it working; a cancellation nobody asked for is unresolved.
/// </summary>
internal enum TInvestCancelReason
{
    Owner,
    CarryOver
}