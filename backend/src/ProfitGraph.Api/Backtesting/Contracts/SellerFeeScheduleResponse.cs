namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record SellerFeeScheduleResponse(IReadOnlyList<string> Rates, string Minimum);