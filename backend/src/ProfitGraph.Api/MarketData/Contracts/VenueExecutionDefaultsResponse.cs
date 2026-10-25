using ProfitGraph.Api.Backtesting.Contracts;

namespace ProfitGraph.Api.MarketData.Contracts;

/// <summary>
///     What a new backtest on the venue starts from; the request still carries every value it uses.
/// </summary>
public sealed record VenueExecutionDefaultsResponse(SellerFeeScheduleResponse? SellerFees);