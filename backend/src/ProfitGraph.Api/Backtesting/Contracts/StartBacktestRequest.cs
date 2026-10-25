namespace ProfitGraph.Api.Backtesting.Contracts;

/// <summary>
///     Starts a run of a strategy version; decimals are exact invariant strings. Omitted parameters keep the version's
///     values, an omitted version is the current one and an omitted holdout fraction is 0.2. Seller fees replace the limit
///     and market rates (ADR 0022).
/// </summary>
public sealed record StartBacktestRequest(
    Guid StrategyId,
    int? Version,
    string? Venue,
    string? Symbol,
    string? Timeframe,
    DateTimeOffset? From,
    DateTimeOffset? To,
    IReadOnlyDictionary<string, string>? Parameters,
    string? StartingCapital,
    string? Slippage,
    string? LimitFeeRate,
    string? MarketFeeRate,
    string? OutOfSampleFraction,
    SellerFeeScheduleRequest? SellerFees = null);