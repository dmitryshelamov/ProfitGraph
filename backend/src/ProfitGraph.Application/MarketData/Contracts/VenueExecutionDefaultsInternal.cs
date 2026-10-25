using ProfitGraph.Application.Backtesting.Contracts;

namespace ProfitGraph.Application.MarketData.Contracts;

/// <summary>
///     How a new backtest on a venue executes unless the request says otherwise (ADR 0022, C4).
/// </summary>
public sealed record VenueExecutionDefaultsInternal(SellerFeeScheduleInternal? SellerFees);