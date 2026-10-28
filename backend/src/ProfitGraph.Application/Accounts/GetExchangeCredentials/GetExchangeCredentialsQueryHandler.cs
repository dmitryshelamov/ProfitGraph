using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Converters;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;

namespace ProfitGraph.Application.Accounts.GetExchangeCredentials;

/// <summary>
///     A credential stored before its public identifier was recorded shows the value of its venue's public field, decrypted
///     for this answer only; no secret field is ever read for it.
/// </summary>
internal sealed class GetExchangeCredentialsQueryHandler(
    IExchangeCredentialRepository credentials,
    ISecretProtector protector,
    VenuePorts<IVenueCredentialSchema> schemas) : IRequestHandler<GetExchangeCredentialsQuery, IReadOnlyList<ExchangeCredentialInternal>>
{
    public async Task<IReadOnlyList<ExchangeCredentialInternal>> Handle(GetExchangeCredentialsQuery query, CancellationToken cancellationToken)
    {
        protector.EnsureAvailable();
        IReadOnlyList<ExchangeCredential> stored = await credentials.List(new UserId(query.OwnerId), cancellationToken);

        return [.. stored.Select(credential => credential.ToInternal() with { PublicId = credential.PublicId?.Value ?? PublicValue(credential) })];
    }

    private string? PublicValue(ExchangeCredential credential)
    {
        if (schemas.Find(credential.Venue)?.PublicField is not { } field || !credential.Secrets.TryGetValue(field, out EncryptedSecret? secret))
        {
            return null;
        }

        try
        {
            return protector.Decrypt(credential.Id, credential.OwnerId, field, secret);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }
}