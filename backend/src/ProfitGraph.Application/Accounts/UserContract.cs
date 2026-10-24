using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts;

public sealed record UserContract(Guid Id, string Login, UserRole Role, UserStatus Status)
{
    public static UserContract From(User user)
    {
        return new UserContract(user.Id, user.Login, user.Role, user.Status);
    }
}