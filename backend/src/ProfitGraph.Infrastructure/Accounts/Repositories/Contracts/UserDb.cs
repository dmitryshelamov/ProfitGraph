namespace ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

internal sealed class UserDb
{
    public Guid Id { get; init; }
    public string Login { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}