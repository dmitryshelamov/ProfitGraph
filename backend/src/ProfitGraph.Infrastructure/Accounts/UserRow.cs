using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Infrastructure.Accounts;

internal sealed record UserRow(
    Guid Id,
    string Login,
    string PasswordHash,
    string Role,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static UserRow From(User user)
    {
        return new UserRow(user.Id, user.Login, user.PasswordHash, user.Role.ToString(), user.Status.ToString(), user.CreatedAt.UtcDateTime, user.UpdatedAt.UtcDateTime);
    }

    public User ToDomain()
    {
        return User.Restore(Id, Login, PasswordHash, Enum.Parse<UserRole>(Role), Enum.Parse<UserStatus>(Status), Utc(CreatedAt), Utc(UpdatedAt));
    }

    // Npgsql reads timestamptz as a UTC DateTime.
    private static DateTimeOffset Utc(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}