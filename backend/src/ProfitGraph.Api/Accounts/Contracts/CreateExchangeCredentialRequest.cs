namespace ProfitGraph.Api.Accounts.Contracts;

public sealed record CreateExchangeCredentialRequest(string? Venue, string? Label, string? ApiKey, string? ApiSecret)
{
    public override string ToString()
    {
        return $"CreateExchangeCredentialRequest {{ Venue = {Venue}, Redacted }}";
    }
}