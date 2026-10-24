using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Api.Accounts.Contracts;

public sealed record UserResponse(Guid Id, string Login, UserRole Role, UserStatus Status);