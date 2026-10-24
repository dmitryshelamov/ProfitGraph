namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record BacktestRejectionResponse(string Reason, string Message, string Side, string Type, string? Tag, long? OrderId);