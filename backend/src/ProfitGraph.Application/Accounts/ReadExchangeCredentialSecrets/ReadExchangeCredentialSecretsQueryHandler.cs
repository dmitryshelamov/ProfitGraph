using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;

internal sealed class ReadExchangeCredentialSecretsQueryHandler(IExchangeCredentialRepository credentials, ISecretProtector protector)
    : IRequestHandler<ReadExchangeCredentialSecretsQuery, ExchangeCredentialSecretsInternal>
{
    public async Task<ExchangeCredentialSecretsInternal> Handle(ReadExchangeCredentialSecretsQuery query, CancellationToken cancellationToken)
    {
        protector.EnsureAvailable();
        ExchangeCredential credential = await credentials.GetOwned(query.Id, query.OwnerId, cancellationToken);

        return protector.Decrypt(credential);
    }
}