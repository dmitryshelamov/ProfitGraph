namespace ProfitGraph.Api.Optimization.Contracts;

public sealed record WalkForwardEquityPointResponse(int CandleIndex, DateTimeOffset? Time, string CloseEquity);