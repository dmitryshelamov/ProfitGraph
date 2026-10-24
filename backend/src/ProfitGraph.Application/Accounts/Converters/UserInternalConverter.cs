using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.Converters;

public static class UserInternalConverter
{
    public static UserInternal ToInternal(this User user)
    {
        return new UserInternal(user.Id, user.Login, user.Role, user.Status);
    }
}