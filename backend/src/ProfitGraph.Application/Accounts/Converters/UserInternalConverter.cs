using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.Converters;

public static class UserInternalConverter
{
    public static UserInternal ToInternal(this User user)
    {
        return new UserInternal(user.Id.Value, user.Login.Value, user.Role, user.Status);
    }
}