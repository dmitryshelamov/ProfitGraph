namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     Marketplace fees paid by the seller per unit (ADR 0022): rates of its components and the smallest fee of each.
/// </summary>
public sealed record SellerFeeScheduleInternal(IReadOnlyList<decimal> Rates, decimal Minimum);