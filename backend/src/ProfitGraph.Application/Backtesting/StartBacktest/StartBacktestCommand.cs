using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Jobs.Contracts;

namespace ProfitGraph.Application.Backtesting.StartBacktest;

/// <summary>
///     Freezes a run of a strategy version and queues it (stage 3 plan, S3-5). Omitted parameters keep the version's values;
///     an omitted holdout fraction is 0.2. Seller fees replace the limit and market rates, which must then be zero.
/// </summary>
public sealed record StartBacktestCommand(
    Guid UserId,
    Guid StrategyId,
    int? Version,
    string Venue,
    string Symbol,
    string Timeframe,
    DateTimeOffset? From,
    DateTimeOffset? To,
    IReadOnlyDictionary<string, decimal>? Parameters,
    decimal StartingCapital,
    decimal Slippage,
    decimal LimitFeeRate,
    decimal MarketFeeRate,
    decimal? OutOfSampleFraction,
    SellerFeeScheduleInternal? SellerFees = null) : ICommand<BackgroundJobInternal>;