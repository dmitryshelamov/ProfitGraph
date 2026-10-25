using Microsoft.Extensions.Options;

using ProfitGraph.Venues.Steam.Confirmations;
using ProfitGraph.Venues.Steam.MarketData;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     Decides an account's Guard status from Steam's own answers (Profit Fox Designer <c>SteamCredentialChecker</c> and ADR
///     0083 with its amendments). <see cref="SteamGuardStatus.Ready" /> needs all of: Steam's time close to ours; the market
///     eligibility check redirecting back into the market; a readable wallet in a supported currency; and signed mobile
///     confirmations listed. <see cref="SteamGuardStatus.Restricted" /> needs Steam's positive refusal: a non-market answer to
///     the eligibility check, a restriction or CAPTCHA, an unsupported wallet, or refused confirmations. Anything that only
///     failed to answer stays <see cref="SteamGuardStatus.Checking" /> and is checked again later.
/// </summary>
internal sealed class SteamEligibility(
    SteamAccountSessions sessions,
    SteamMarketDataAccount marketData,
    SteamConfirmationClient confirmations,
    IOptions<SteamOptions> options,
    TimeProvider clock)
{
    public const string EligibilityPath = "/market/eligibilitycheck/?goto=%2Fmarket%2F";

    public async Task<SteamGuardStatus> Check(Guid ownerId, Guid credentialId, CancellationToken cancellationToken)
    {
        SteamAccountSession session = sessions.For(ownerId, credentialId);

        try
        {
            DateTimeOffset serverTime = await session.ServerTime(cancellationToken);

            if ((serverTime - clock.GetUtcNow()).Duration() > options.Value.ClockTolerance)
            {
                return SteamGuardStatus.Checking;
            }
        }
        catch (SteamSessionException)
        {
            return SteamGuardStatus.Checking;
        }

        SteamRequestResult eligibility = await session.Execute(
            new SteamHttpRequest(SteamEndpoint.Community, EligibilityPath, Class: SteamRequestClass.Account, ExpectsRedirect: true),
            cancellationToken);

        if (Eligible(eligibility) is not true)
        {
            return Eligible(eligibility) is false ? SteamGuardStatus.Restricted : SteamGuardStatus.Checking;
        }

        try
        {
            await marketData.WalletOf(credentialId, cancellationToken);
        }
        catch (SteamMarketDataException exception)
        {
            return exception.Unavailable ? SteamGuardStatus.Checking : SteamGuardStatus.Restricted;
        }

        return await confirmations.Probe(session, credentialId, cancellationToken) switch
        {
            true => SteamGuardStatus.Ready,
            false => SteamGuardStatus.Restricted,
            null => SteamGuardStatus.Checking
        };
    }

    /// <summary>
    ///     True when the check redirected back into the market, false when Steam answered something else, null when it did not
    ///     answer (Profit Fox Designer <c>SteamMarketEligibility.IsAllowed</c>).
    /// </summary>
    private bool? Eligible(SteamRequestResult result)
    {
        if (result.Outcome is SteamRequestOutcome.Restricted or SteamRequestOutcome.Captcha or SteamRequestOutcome.Forbidden)
        {
            return false;
        }

        if (result.Response is not { } response || result.Outcome is SteamRequestOutcome.Unauthorized or SteamRequestOutcome.RateLimited or
                SteamRequestOutcome.TransientFailure or SteamRequestOutcome.NotSent or SteamRequestOutcome.Deferred)
        {
            return null;
        }

        Uri community = options.Value.Address(SteamEndpoint.Community);

        if (response.StatusCode is not (302 or 303 or 307 or 308) || response.Location is not { } location || location.OriginalString.Contains('\\') ||
            !Uri.TryCreate(community, location, out Uri? destination))
        {
            return false;
        }

        string path = Uri.UnescapeDataString(destination.AbsolutePath);

        return destination.Scheme == community.Scheme && destination.Authority == community.Authority && destination.UserInfo.Length == 0 &&
            destination.Fragment.Length == 0 && path.StartsWith("/market/", StringComparison.OrdinalIgnoreCase) && !path.Contains('\\') &&
            !path.Contains("eligibilitycheck", StringComparison.OrdinalIgnoreCase) && !path.Contains("/login", StringComparison.OrdinalIgnoreCase);
    }
}