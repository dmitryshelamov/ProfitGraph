namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record BacktestLedgerEntryResponse(
    long Sequence,
    string Kind,
    int CandleIndex,
    DateTimeOffset CandleOpenTime,
    string Phase,
    string MarketPrice,
    BacktestOrderResponse? Order,
    BacktestFillResponse? Fill,
    BacktestRejectionResponse? Rejection);