namespace ProfitGraph.Domain.Optimization;

public sealed record WalkForwardEquityPoint(int CandleIndex, decimal CloseEquity, DateTimeOffset? Time = null);