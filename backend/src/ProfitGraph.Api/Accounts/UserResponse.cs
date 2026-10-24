using ProfitGraph.Application.Accounts;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Api.Accounts;

public sealed record UserResponse(Guid Id, string Login, UserRole Role, UserStatus Status)
{
    public static UserResponse From(UserContract user)
    {
        return new UserResponse(user.Id, user.Login, user.Role, user.Status);
    }
}