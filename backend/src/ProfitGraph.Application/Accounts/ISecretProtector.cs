using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts;

/// <summary>
///     Encrypts credential secrets at rest (ADR 0015). Each value is bound to its credential, owner and field, so a value
///     copied into another row or field cannot be decrypted.
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    ///     False when no active master key is configured; credential use cases then answer <c>credentials_unavailable</c>.
    /// </summary>
    bool IsAvailable { get; }

    EncryptedSecret Encrypt(ExchangeCredentialId credentialId, UserId ownerId, string field, string plaintext);

    string Decrypt(ExchangeCredentialId credentialId, UserId ownerId, string field, EncryptedSecret secret);
}