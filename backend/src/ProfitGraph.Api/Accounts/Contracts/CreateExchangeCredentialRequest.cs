namespace ProfitGraph.Api.Accounts.Contracts;

/// <summary>
///     An API key and secret, or for a venue whose credential selects an account the venue's declared fields
///     (ADR 0023, TC5). Values are never printed.
/// </summary>
public sealed record CreateExchangeCredentialRequest(string? Venue, string? Label, string? ApiKey, string? ApiSecret, IReadOnlyDictionary<string, string>? Fields = null)
{
    public override string ToString()
    {
        return $"CreateExchangeCredentialRequest {{ Venue = {Venue}, Redacted }}";
    }
}