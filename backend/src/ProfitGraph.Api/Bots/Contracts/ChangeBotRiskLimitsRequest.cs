namespace ProfitGraph.Api.Bots.Contracts;

public sealed record ChangeBotRiskLimitsRequest(string? MaxOrderQuote, string? DailyLossLimitQuote);