using MediatR;

using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.BootstrapOwner;

internal sealed class BootstrapOwnerCommandHandler(IUserRepository users, IPasswordHasher passwords, TimeProvider clock) : IRequestHandler<BootstrapOwnerCommand, bool>
{
    public Task<bool> Handle(BootstrapOwnerCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        var owner = User.Create(Guid.NewGuid(), command.Login, UserRole.Owner, now);
        owner.SetPasswordHash(passwords.Hash(owner, command.Password), now);

        return users.TryAddFirst(owner, cancellationToken);
    }
}