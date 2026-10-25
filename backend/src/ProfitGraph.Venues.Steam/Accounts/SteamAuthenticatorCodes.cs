using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     The current Steam Guard code of an imported or enrolled authenticator, for Steam's time as the account's session
///     measured it (ADR 0022). Nothing is stored, logged or sent anywhere but in the answer.
/// </summary>
internal sealed class SteamAuthenticatorCodes(SteamAccountSessions sessions, IServiceScopeFactory scopes, TimeProvider clock) : IVenueAuthenticatorCodes
{
    public VenueId Venue => SteamVenue.VenueId;

    public async Task<AuthenticatorCodeInternal> Current(Guid ownerId, Guid credentialId, CancellationToken cancellationToken)
    {
        DateTimeOffset serverTime = await sessions.For(ownerId, credentialId).ServerTime(cancellationToken);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ExchangeCredentialSecretsInternal secrets = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ReadExchangeCredentialSecretsQuery(ownerId, credentialId), cancellationToken);
        var windowSeconds = (long)SteamGuardCode.Window.TotalSeconds;
        DateTimeOffset windowEnd = DateTimeOffset.FromUnixTimeSeconds((serverTime.ToUnixTimeSeconds() / windowSeconds + 1) * windowSeconds);

        // The window ends by Steam's clock; the answer says when that is by ours.
        return new AuthenticatorCodeInternal(
            SteamGuardCode.For(secrets.Field(SteamCredentialFields.SharedSecret), serverTime),
            clock.GetUtcNow() + (windowEnd - serverTime));
    }
}