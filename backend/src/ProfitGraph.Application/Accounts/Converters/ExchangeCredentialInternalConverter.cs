using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.Converters;

public static class ExchangeCredentialInternalConverter
{
    public static ExchangeCredentialInternal ToInternal(this ExchangeCredential credential)
    {
        return new ExchangeCredentialInternal(credential.Id.Value, credential.Venue.Value, credential.Label.Value, credential.ApiKeyLast4.Value, credential.CreatedAt);
    }
}