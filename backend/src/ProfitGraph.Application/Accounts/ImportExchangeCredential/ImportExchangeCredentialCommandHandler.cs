using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.Accounts.ImportExchangeCredential;

internal sealed class ImportExchangeCredentialCommandHandler(
    IUserRepository users,
    IExchangeCredentialRepository credentials,
    ISecretProtector protector,
    TimeProvider clock)
    : IRequestHandler<ImportExchangeCredentialCommand, ExchangeCredentialInternal>
{
    public async Task<ExchangeCredentialInternal> Handle(ImportExchangeCredentialCommand command, CancellationToken cancellationToken)
    {
        protector.EnsureAvailable();
        User owner = await users.GetByLogin(new UserLogin(command.OwnerLogin), cancellationToken) is { IsActive: true } active
            ? active
            : throw new ResourceNotFoundException("No active user has that login.", "user_not_found");
        var id = ExchangeCredentialId.New();
        var credential = ExchangeCredential.Create(
            id,
            owner.Id,
            new VenueId(command.Venue),
            new ExchangeCredentialLabel(command.Label),
            new ApiKeyLast4(command.PublicHint),
            command.Fields.ToDictionary(field => field.Key, field => protector.Encrypt(id, owner.Id, field.Key, field.Value), StringComparer.Ordinal),
            clock.GetUtcNow(),
            publicId: command.PublicId is null ? null : new CredentialPublicId(command.PublicId));
        await credentials.Add(credential, cancellationToken);

        return credential.ToInternal();
    }
}