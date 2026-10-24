namespace ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

internal sealed class ExchangeCredentialDb
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string Venue { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string ApiKeyLast4 { get; init; } = string.Empty;
    public string ApiKeyKeyId { get; init; } = string.Empty;
    public byte[] ApiKeyNonce { get; init; } = [];
    public byte[] ApiKeyCiphertext { get; init; } = [];
    public byte[] ApiKeyTag { get; init; } = [];
    public string ApiSecretKeyId { get; init; } = string.Empty;
    public byte[] ApiSecretNonce { get; init; } = [];
    public byte[] ApiSecretCiphertext { get; init; } = [];
    public byte[] ApiSecretTag { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
}