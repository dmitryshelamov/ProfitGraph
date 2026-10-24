using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.BlockUser;

internal sealed class BlockUserCommandHandler(IUserRepository users, TimeProvider clock) : IRequestHandler<BlockUserCommand>
{
    public async Task Handle(BlockUserCommand command, CancellationToken cancellationToken)
    {
        int activeOwners = await users.CountActiveOwnersForUpdate(cancellationToken);
        User user = (command.UserId == Guid.Empty ? null : await users.GetById(new UserId(command.UserId), cancellationToken))
            ?? throw new ResourceNotFoundException("The user does not exist.", "user_not_found");

        user.Block(new UserId(command.ActorId), activeOwners, clock.GetUtcNow());
        await users.Update(user, cancellationToken);
    }
}