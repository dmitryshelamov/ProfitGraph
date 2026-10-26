namespace ProfitGraph.Api.Backtesting.Contracts;

/// <summary>
///     Marketplace fees paid by the seller per unit instead of the limit and market rates (ADR 0022); decimals are exact
///     invariant strings. The increment is the step of every seller amount and fee in whole minor units of the quote asset,
///     one when omitted: a wallet that counts only whole units of a two-decimal currency has a hundred.
/// </summary>
public sealed record SellerFeeScheduleRequest(IReadOnlyList<string>? Rates, string? Minimum, long? Increment = null);