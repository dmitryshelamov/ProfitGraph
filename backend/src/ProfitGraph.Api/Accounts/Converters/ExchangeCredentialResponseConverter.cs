using ProfitGraph.Api.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Contracts;

namespace ProfitGraph.Api.Accounts.Converters;

internal static class ExchangeCredentialResponseConverter
{
    public static ExchangeCredentialResponse ToResponse(this ExchangeCredentialInternal credential)
    {
        return new ExchangeCredentialResponse(credential.Id, credential.Venue, credential.Label, credential.ApiKeyLast4, credential.CreatedAt);
    }
}