namespace ProfitGraph.Api.Backtesting.Contracts;

/// <summary>
///     Marketplace fees paid by the seller per unit instead of the limit and market rates (ADR 0022); decimals are exact
///     invariant strings.
/// </summary>
public sealed record SellerFeeScheduleRequest(IReadOnlyList<string>? Rates, string? Minimum);