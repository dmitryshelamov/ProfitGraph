namespace ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

/// <summary>
///     One encrypted value inside <c>exchange_credentials.secrets</c>; byte arrays are base64 in JSON.
/// </summary>
internal sealed class EncryptedSecretDb
{
    public string KeyId { get; init; } = string.Empty;
    public byte[] Nonce { get; init; } = [];
    public byte[] Ciphertext { get; init; } = [];
    public byte[] Tag { get; init; } = [];
}