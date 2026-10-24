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

        return new ExchangeCredentialSecretsInternal(
            credential.Id.Value,
            credential.Venue.Value,
            protector.Decrypt(credential.Id, credential.OwnerId, ExchangeCredentialFields.ApiKey, credential.ApiKey),
            protector.Decrypt(credential.Id, credential.OwnerId, ExchangeCredentialFields.ApiSecret, credential.ApiSecret));
    }
}