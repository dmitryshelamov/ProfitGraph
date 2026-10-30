namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed class ScanCardOddsSourceDb
{
    public Guid Id { get; init; }
    public string PresetName { get; init; } = string.Empty;
    public DateTimeOffset PeriodFrom { get; init; }
    public DateTimeOffset PeriodTo { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public string CardOddsJson { get; init; } = "[]";
}