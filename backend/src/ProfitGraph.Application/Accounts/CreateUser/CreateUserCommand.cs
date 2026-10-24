using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.CreateUser;

public sealed record CreateUserCommand(string Login, string Password, UserRole Role) : ICommand<UserContract>;