namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record BacktestConfigurationResponse(
    string Venue,
    string Symbol,
    string Timeframe,
    DateTimeOffset? From,
    DateTimeOffset? To,
    DateTimeOffset ObservedAt,
    IReadOnlyDictionary<string, string> Parameters,
    string StartingCapital,
    string Slippage,
    string LimitFeeRate,
    string MarketFeeRate,
    string OutOfSampleFraction,
    int WarmupCandles,
    SellerFeeScheduleResponse? SellerFees,
    long? SettlementHoldSeconds,
    string LimitFill);