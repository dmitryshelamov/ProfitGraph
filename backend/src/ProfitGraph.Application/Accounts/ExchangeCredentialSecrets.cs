using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts;

internal static class ExchangeCredentialSecrets
{
    /// <summary>
    ///     Decrypts both values of a credential in memory for a venue call.
    /// </summary>
    internal static ExchangeCredentialSecretsInternal Decrypt(this ISecretProtector protector, ExchangeCredential credential)
    {
        protector.EnsureAvailable();

        return new ExchangeCredentialSecretsInternal(
            credential.Id.Value,
            credential.Venue.Value,
            protector.Decrypt(credential.Id, credential.OwnerId, ExchangeCredentialFields.ApiKey, credential.ApiKey),
            protector.Decrypt(credential.Id, credential.OwnerId, ExchangeCredentialFields.ApiSecret, credential.ApiSecret));
    }
}