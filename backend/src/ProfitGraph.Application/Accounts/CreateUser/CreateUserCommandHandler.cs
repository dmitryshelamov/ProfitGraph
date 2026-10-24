using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.CreateUser;

internal sealed class CreateUserCommandHandler(IUserRepository users, IPasswordHasher passwords, TimeProvider clock) : IRequestHandler<CreateUserCommand, UserInternal>
{
    public async Task<UserInternal> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        var user = User.Create(Guid.NewGuid(), command.Login, command.Role, now);
        user.SetPasswordHash(passwords.Hash(user, command.Password), now);

        if (!await users.TryAdd(user, cancellationToken))
        {
            throw new StateConflictException("The login is already taken.", "login_taken");
        }

        return user.ToInternal();
    }
}