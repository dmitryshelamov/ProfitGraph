namespace ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

internal sealed class ExchangeCredentialDb
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string Venue { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string ApiKeyLast4 { get; init; } = string.Empty;

    /// <summary>
    ///     The encrypted values as JSON: field name to <see cref="EncryptedSecretDb" />.
    /// </summary>
    public string Secrets { get; init; } = "{}";

    public DateTimeOffset CreatedAt { get; init; }
    public string? AccountFingerprint { get; init; }
}