namespace ProfitGraph.Application.Backtesting.Contracts;

public sealed record BacktestRejectionInternal(string Reason, string Message, string Side, string Type, string? Tag, long? OrderId);