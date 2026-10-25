using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Converters;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.Accounts.CreateExchangeCredential;

internal sealed class CreateExchangeCredentialCommandHandler(IExchangeCredentialRepository credentials, ISecretProtector protector, TimeProvider clock)
    : IRequestHandler<CreateExchangeCredentialCommand, ExchangeCredentialInternal>
{
    public async Task<ExchangeCredentialInternal> Handle(CreateExchangeCredentialCommand command, CancellationToken cancellationToken)
    {
        protector.EnsureAvailable();
        var id = ExchangeCredentialId.New();
        var ownerId = new UserId(command.OwnerId);
        string apiKey = command.ApiKey!.Trim();
        var credential = ExchangeCredential.Create(
            id,
            ownerId,
            new VenueId(command.Venue!),
            new ExchangeCredentialLabel(command.Label),
            ApiKeyLast4.Of(apiKey),
            new Dictionary<string, EncryptedSecret>(StringComparer.Ordinal)
            {
                [ExchangeCredentialFields.ApiKey] = protector.Encrypt(id, ownerId, ExchangeCredentialFields.ApiKey, apiKey),
                [ExchangeCredentialFields.ApiSecret] = protector.Encrypt(id, ownerId, ExchangeCredentialFields.ApiSecret, command.ApiSecret!.Trim())
            },
            clock.GetUtcNow());
        await credentials.Add(credential, cancellationToken);

        return credential.ToInternal();
    }
}