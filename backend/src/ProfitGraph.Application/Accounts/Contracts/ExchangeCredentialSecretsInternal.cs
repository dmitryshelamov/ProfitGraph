namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     Decrypted secrets for a venue gateway. They stay in memory; <see cref="ToString" /> never prints them.
/// </summary>
public sealed record ExchangeCredentialSecretsInternal(Guid CredentialId, string Venue, string ApiKey, string ApiSecret)
{
    public override string ToString()
    {
        return $"ExchangeCredentialSecretsInternal {{ CredentialId = {CredentialId}, Venue = {Venue}, Redacted }}";
    }
}