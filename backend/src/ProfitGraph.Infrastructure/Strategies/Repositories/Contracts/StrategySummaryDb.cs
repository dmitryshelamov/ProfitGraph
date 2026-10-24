namespace ProfitGraph.Infrastructure.Strategies.Repositories.Contracts;

internal sealed class StrategySummaryDb
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int CurrentVersion { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? TemplateKind { get; init; }
}