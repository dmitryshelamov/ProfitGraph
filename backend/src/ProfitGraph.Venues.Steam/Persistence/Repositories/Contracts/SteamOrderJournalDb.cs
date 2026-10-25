namespace ProfitGraph.Venues.Steam.Persistence.Repositories.Contracts;

internal sealed class SteamOrderJournalDb
{
    public string ClientOrderId { get; init; } = string.Empty;
    public Guid BotId { get; init; }
    public Guid CredentialId { get; init; }
    public long EngineOrderId { get; init; }
    public string Instrument { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public string OrderType { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public long PriceMinorUnits { get; init; }
    public string State { get; init; } = string.Empty;
    public string? WaitingCode { get; init; }
    public string? SteamOrderId { get; init; }
    public string? ConfirmationCreator { get; init; }
    public string? Outcome { get; init; }
    public DateTimeOffset? PlaceStartedAt { get; init; }
    public DateTimeOffset? Deadline { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}