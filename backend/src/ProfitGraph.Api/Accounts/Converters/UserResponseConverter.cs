using ProfitGraph.Api.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Contracts;

namespace ProfitGraph.Api.Accounts.Converters;

internal static class UserResponseConverter
{
    public static UserResponse ToResponse(this UserInternal user)
    {
        return new UserResponse(user.Id, user.Login, user.Role, user.Status);
    }
}