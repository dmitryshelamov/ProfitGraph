using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Jobs.Contracts;

namespace ProfitGraph.Application.Backtesting.StartBacktest;

/// <summary>
///     Freezes a run of a strategy version and queues it (stage 3 plan, S3-5). Omitted parameters keep the version's values;
///     an omitted holdout fraction is 0.2. Seller fees replace the limit and market rates, which must then be zero; a
///     settlement hold delays selling what was bought (ADR 0022). A named credential of the user reads market data for a
///     venue without anonymous market data; the job keeps its identifier, the frozen run never does (ADR 0023, TC6).
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
    SellerFeeScheduleInternal? SellerFees = null,
    long? SettlementHoldSeconds = null,
    Guid? CredentialId = null) : ICommand<BackgroundJobInternal>;