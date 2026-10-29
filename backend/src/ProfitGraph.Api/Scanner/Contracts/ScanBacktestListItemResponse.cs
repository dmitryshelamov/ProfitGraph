namespace ProfitGraph.Api.Scanner.Contracts;

public sealed record ScanBacktestListItemResponse(
    Guid Id,
    string PresetName,
    DateTimeOffset From,
    DateTimeOffset To,
    Guid JobId,
    string? JobStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int? Signals);