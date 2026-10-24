namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategySummaryResponse(Guid Id, string Name, int CurrentVersion, DateTimeOffset UpdatedAt, string? TemplateKind);