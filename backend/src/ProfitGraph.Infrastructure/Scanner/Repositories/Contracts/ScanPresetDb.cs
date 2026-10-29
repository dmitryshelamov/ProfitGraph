namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed class ScanPresetDb
{
    public Guid Id { get; init; }
    public Guid OwnerId { get; init; }
    public string Venue { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DefinitionJson { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}