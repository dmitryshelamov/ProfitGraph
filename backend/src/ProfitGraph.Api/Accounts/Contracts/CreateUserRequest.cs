using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Api.Accounts.Contracts;

public sealed record CreateUserRequest(string? Login, string? Password, UserRole Role);