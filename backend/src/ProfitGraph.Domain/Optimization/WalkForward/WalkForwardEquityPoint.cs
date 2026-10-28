namespace ProfitGraph.Domain.Optimization.WalkForward;

public sealed record WalkForwardEquityPoint(int CandleIndex, decimal CloseEquity, DateTimeOffset? Time = null);