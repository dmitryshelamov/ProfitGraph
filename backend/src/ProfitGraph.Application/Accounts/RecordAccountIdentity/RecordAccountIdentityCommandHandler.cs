using MediatR;

using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Accounts.Identities;

namespace ProfitGraph.Application.Accounts.RecordAccountIdentity;

internal sealed class RecordAccountIdentityCommandHandler(IAccountIdentityRepository identities, TimeProvider clock) : IRequestHandler<RecordAccountIdentityCommand>
{
    public async Task Handle(RecordAccountIdentityCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        var credentialId = new ExchangeCredentialId(command.CredentialId);
        AccountIdentity identity = await identities.GetForUpdate(credentialId, cancellationToken) ?? AccountIdentity.New(credentialId);

        if (command.Read.Identity is { } read)
        {
            identity.RecordRead(
                new AccountIdentityDetails(read.AccountId, read.Name, read.Kind, read.AccessLevel, read.ReadOnly, read.Permissions, read.ExpiresAt),
                now);
        }
        else
        {
            identity.RecordFailure(command.Read.FailureReason ?? "unknown", command.Read.FailureDetail, now);
        }

        await identities.Save(identity, cancellationToken);
    }
}