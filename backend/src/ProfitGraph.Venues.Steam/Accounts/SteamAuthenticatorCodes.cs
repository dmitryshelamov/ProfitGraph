using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     The current Steam Guard code of an imported or enrolled authenticator, for Steam's time as the account's session
///     measured it (ADR 0022). Without that time there is no code. Nothing is stored, logged or sent anywhere but in the
///     answer.
/// </summary>
internal sealed class SteamAuthenticatorCodes(SteamAccountSessions sessions, IServiceScopeFactory scopes, IOptions<SteamOptions> options, TimeProvider clock)
    : IVenueAuthenticatorCodes
{
    public VenueId Venue => SteamVenue.VenueId;

    public async Task<AuthenticatorCodeInternal> Current(Guid ownerId, Guid credentialId, CancellationToken cancellationToken)
    {
        DateTimeOffset serverTime = await ServerTime(ownerId, credentialId, cancellationToken);
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

    private async Task<DateTimeOffset> ServerTime(Guid ownerId, Guid credentialId, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            throw new AuthenticatorCodeUnavailableException("Steam is disabled on this server, so its time for the Steam Guard code is unknown.");
        }

        try
        {
            return await sessions.For(ownerId, credentialId).ServerTime(cancellationToken);
        }
        catch (SteamSessionException exception)
        {
            throw new AuthenticatorCodeUnavailableException(
                exception.Outcome == SteamRequestOutcome.NotSent
                    ? "Steam requests of this account are held back now, so its time for the Steam Guard code is unknown."
                    : "Steam did not tell its time for the Steam Guard code; try again later.");
        }
    }
}