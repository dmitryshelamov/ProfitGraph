namespace ProfitGraph.Api.Bots.Contracts;

public sealed record ChangeBotParametersRequest(IReadOnlyDictionary<string, string>? Parameters, string? AllocationCap);