namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Who asked T-Invest to cancel an attempt, recorded before the request leaves (ADR 0023, T12b). An owner or strategy
///     cancel ends the engine order; a carry-over keeps it working; a cancellation nobody asked for is unresolved, except
///     <see cref="Expired" />: a DAY limit order T-Invest reported cancelled once its trading day had ended, which the exchange
///     ended itself and which ends the engine order with what it executed (ADR 0031).
/// </summary>
internal enum TInvestCancelReason
{
    Owner,
    CarryOver,
    Expired
}