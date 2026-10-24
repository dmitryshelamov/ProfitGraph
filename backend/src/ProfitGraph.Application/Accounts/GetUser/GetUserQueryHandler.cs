using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Converters;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.GetUser;

internal sealed class GetUserQueryHandler(IUserRepository users) : IRequestHandler<GetUserQuery, UserInternal?>
{
    public async Task<UserInternal?> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        User? user = await users.GetById(query.Id, cancellationToken);

        return user is null ? null : user.ToInternal();
    }
}