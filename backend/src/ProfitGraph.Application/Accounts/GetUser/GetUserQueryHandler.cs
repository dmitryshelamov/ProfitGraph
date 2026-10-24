using MediatR;

using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.GetUser;

internal sealed class GetUserQueryHandler(IUserRepository users) : IRequestHandler<GetUserQuery, UserContract?>
{
    public async Task<UserContract?> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        User? user = await users.GetById(query.Id, cancellationToken);

        return user is null ? null : UserContract.From(user);
    }
}