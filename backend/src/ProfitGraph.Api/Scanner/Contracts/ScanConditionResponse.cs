namespace ProfitGraph.Api.Scanner.Contracts;

public sealed record ScanConditionResponse(string Metric, string? Minimum, string? Maximum);