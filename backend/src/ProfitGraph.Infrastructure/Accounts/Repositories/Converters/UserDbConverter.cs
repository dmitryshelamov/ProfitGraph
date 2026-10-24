using ProfitGraph.Domain.Accounts;
using ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Accounts.Repositories.Converters;

internal static class UserDbConverter
{
    public static User ToDomain(this UserDb row)
    {
        return User.Restore(
            row.Id,
            row.Login,
            row.PasswordHash,
            Enum.Parse<UserRole>(row.Role),
            Enum.Parse<UserStatus>(row.Status),
            row.CreatedAt,
            row.UpdatedAt);
    }

    public static UserDb ToDb(this User user)
    {
        return new UserDb
        {
            Id = user.Id,
            Login = user.Login,
            PasswordHash = user.PasswordHash,
            Role = user.Role.ToString(),
            Status = user.Status.ToString(),
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }
}