using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;

namespace ProfitGraph.Application.Accounts;

/// <summary>
///     Loads a credential for its owner. Another user's credential is reported as missing, so its existence is not revealed.
/// </summary>
internal static class OwnedExchangeCredentials
{
    internal static async Task<ExchangeCredential> GetOwned(
        this IExchangeCredentialRepository repository,
        Guid id,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        ExchangeCredential? credential = id == Guid.Empty ? null : await repository.Get(new ExchangeCredentialId(id), cancellationToken);

        return credential is not null && credential.IsOwnedBy(new UserId(ownerId))
            ? credential
            : throw NotFound();
    }

    internal static ResourceNotFoundException NotFound()
    {
        return new ResourceNotFoundException("The exchange credential does not exist.", ExchangeCredentialErrorCodes.NotFound);
    }

    internal static void EnsureAvailable(this ISecretProtector protector)
    {
        if (!protector.IsAvailable)
        {
            throw new CredentialsUnavailableException();
        }
    }
}