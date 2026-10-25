using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts;

internal static class ExchangeCredentialSecrets
{
    /// <summary>
    ///     Decrypts every value of a credential in memory for a venue call.
    /// </summary>
    internal static ExchangeCredentialSecretsInternal Decrypt(this ISecretProtector protector, ExchangeCredential credential)
    {
        protector.EnsureAvailable();

        return new ExchangeCredentialSecretsInternal(
            credential.Id.Value,
            credential.Venue.Value,
            credential.Secrets.ToDictionary(
                secret => secret.Key,
                secret => protector.Decrypt(credential.Id, credential.OwnerId, secret.Key, secret.Value),
                StringComparer.Ordinal));
    }
}