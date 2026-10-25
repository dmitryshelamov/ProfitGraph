namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     Decrypted secrets for a venue call, by field name. They stay in memory; <see cref="ToString" /> never prints them.
/// </summary>
public sealed record ExchangeCredentialSecretsInternal(Guid CredentialId, string Venue, IReadOnlyDictionary<string, string> Fields)
{
    public string ApiKey => Field(ExchangeCredentialFields.ApiKey);

    public string ApiSecret => Field(ExchangeCredentialFields.ApiSecret);

    /// <summary>
    ///     The secrets of a venue whose credential is an API key and secret.
    /// </summary>
    public ExchangeCredentialSecretsInternal(Guid credentialId, string venue, string apiKey, string apiSecret)
        : this(
            credentialId,
            venue,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ExchangeCredentialFields.ApiKey] = apiKey,
                [ExchangeCredentialFields.ApiSecret] = apiSecret
            })
    {
    }

    /// <summary>
    ///     One decrypted value; a missing field names only the field.
    /// </summary>
    public string Field(string name)
    {
        return Fields.TryGetValue(name, out string? value) ? value : throw new InvalidOperationException($"The credential has no field '{name}'.");
    }

    public override string ToString()
    {
        return $"ExchangeCredentialSecretsInternal {{ CredentialId = {CredentialId}, Venue = {Venue}, Redacted }}";
    }
}