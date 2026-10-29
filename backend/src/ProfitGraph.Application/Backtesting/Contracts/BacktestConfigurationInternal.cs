namespace ProfitGraph.Application.Backtesting.Contracts;

public sealed record BacktestConfigurationInternal(
    string Venue,
    string Symbol,
    string Timeframe,
    DateTimeOffset? From,
    DateTimeOffset? To,
    DateTimeOffset ObservedAt,
    IReadOnlyDictionary<string, decimal> Parameters,
    decimal StartingCapital,
    decimal Slippage,
    decimal LimitFeeRate,
    decimal MarketFeeRate,
    decimal OutOfSampleFraction,
    int WarmupCandles,
    SellerFeeScheduleInternal? SellerFees,
    long? SettlementHoldSeconds,
    string LimitFill);