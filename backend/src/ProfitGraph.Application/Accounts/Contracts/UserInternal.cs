using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.Contracts;

public sealed record UserInternal(Guid Id, string Login, UserRole Role, UserStatus Status);