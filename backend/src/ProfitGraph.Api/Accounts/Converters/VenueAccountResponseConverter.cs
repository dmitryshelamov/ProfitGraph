using ProfitGraph.Api.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Contracts;

namespace ProfitGraph.Api.Accounts.Converters;

internal static class VenueAccountResponseConverter
{
    public static VenueAccountResponse ToResponse(this VenueAccountInternal account)
    {
        return new VenueAccountResponse(account.Id, account.Name, account.Kind, account.Eligible, account.Ineligibility);
    }
}