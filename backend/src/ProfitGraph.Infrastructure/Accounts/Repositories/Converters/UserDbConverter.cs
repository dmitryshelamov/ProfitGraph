using ProfitGraph.Domain.Accounts;
using ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Accounts.Repositories.Converters;

internal static class UserDbConverter
{
    public static User ToDomain(this UserDb row)
    {
        return User.Restore(
            new UserId(row.Id),
            new UserLogin(row.Login),
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
            Id = user.Id.Value,
            Login = user.Login.Value,
            PasswordHash = user.PasswordHash,
            Role = user.Role.ToString(),
            Status = user.Status.ToString(),
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }
}