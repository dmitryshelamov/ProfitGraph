using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Converters;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.GetExchangeCredentials;

internal sealed class GetExchangeCredentialsQueryHandler(IExchangeCredentialRepository credentials, ISecretProtector protector)
    : IRequestHandler<GetExchangeCredentialsQuery, IReadOnlyList<ExchangeCredentialInternal>>
{
    public async Task<IReadOnlyList<ExchangeCredentialInternal>> Handle(GetExchangeCredentialsQuery query, CancellationToken cancellationToken)
    {
        protector.EnsureAvailable();
        IReadOnlyList<ExchangeCredential> stored = await credentials.List(new UserId(query.OwnerId), cancellationToken);

        return [.. stored.Select(credential => credential.ToInternal())];
    }
}