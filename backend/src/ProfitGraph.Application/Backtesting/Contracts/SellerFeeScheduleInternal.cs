namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     Marketplace fees paid by the seller per unit (ADR 0022): rates of its components, the smallest fee of each and the
///     step of seller amounts and fees in minor units of the quote asset.
/// </summary>
public sealed record SellerFeeScheduleInternal(IReadOnlyList<decimal> Rates, decimal Minimum, long Increment = 1);