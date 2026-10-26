namespace ProfitGraph.Api.Backtesting.Contracts;

/// <summary>
///     Seller fees as the run, bot or venue default uses them; the increment is in whole minor units of the quote asset.
/// </summary>
public sealed record SellerFeeScheduleResponse(IReadOnlyList<string> Rates, string Minimum, long Increment);