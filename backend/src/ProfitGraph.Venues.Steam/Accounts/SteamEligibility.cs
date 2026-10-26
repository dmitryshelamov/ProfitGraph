using Microsoft.Extensions.Logging;
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
///     failed to answer stays <see cref="SteamGuardStatus.Checking" /> and is checked again later. A check that does not make
///     the account ready logs its stage and Steam's answer: outcomes, status codes and static path segments only, never a
///     cookie, token, body or Steam ID (Profit Fox Designer <c>SteamWalletClient.LogFailure</c>).
/// </summary>
internal sealed class SteamEligibility(
    SteamAccountSessions sessions,
    SteamMarketDataAccount marketData,
    SteamConfirmationClient confirmations,
    IOptions<SteamOptions> options,
    TimeProvider clock,
    ILogger<SteamEligibility> logger)
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
                return Logged(SteamGuardStatus.Checking, "Clock", "FarFromSteam");
            }
        }
        catch (SteamSessionException exception)
        {
            return Logged(SteamGuardStatus.Checking, "ServerTime", exception.Outcome.ToString());
        }

        SteamRequestResult eligibility = await session.Execute(
            new SteamHttpRequest(SteamEndpoint.Community, EligibilityPath, Class: SteamRequestClass.Account, ExpectsRedirect: true),
            cancellationToken);

        if (Eligible(eligibility) is not true)
        {
            return Logged(
                Eligible(eligibility) is false ? SteamGuardStatus.Restricted : SteamGuardStatus.Checking,
                "Eligibility",
                eligibility.Outcome.ToString(),
                eligibility.Response?.StatusCode,
                SafePath(eligibility.Response?.Location));
        }

        try
        {
            await marketData.WalletOf(credentialId, cancellationToken);
        }
        catch (SteamMarketDataException exception)
        {
            return Logged(exception.Unavailable ? SteamGuardStatus.Checking : SteamGuardStatus.Restricted, "Wallet", exception.Message);
        }

        return await confirmations.Probe(session, credentialId, cancellationToken) switch
        {
            true => SteamGuardStatus.Ready,
            false => Logged(SteamGuardStatus.Restricted, "Confirmations", "Refused"),
            null => Logged(SteamGuardStatus.Checking, "Confirmations", "NoAnswer")
        };
    }

    private SteamGuardStatus Logged(SteamGuardStatus status, string stage, string outcome, int? httpStatus = null, string? redirectPath = null)
    {
        logger.LogWarning(
            "A Steam account check ended {Status} at {Stage}: {Outcome}, HTTP {HttpStatus}, redirect {RedirectPath}.",
            status,
            stage,
            outcome,
            httpStatus,
            redirectPath);

        return status;
    }

    /// <summary>
    ///     A redirect's path with every segment but Steam's known static ones redacted, since a path can carry an account ID
    ///     or a token (Profit Fox Designer <c>SteamWalletClient.SafeRedirectPath</c>).
    /// </summary>
    private string? SafePath(Uri? location)
    {
        if (location is null || !Uri.TryCreate(options.Value.Address(SteamEndpoint.Community), location, out Uri? resolved))
        {
            return null;
        }

        string[] segments = resolved.AbsolutePath.Split('/');

        for (var index = 0; index < segments.Length; index++)
        {
            if (segments[index] is not ("" or "market" or "login" or "home" or "eligibilitycheck"))
            {
                segments[index] = "[redacted]";
            }
        }

        return (resolved.Authority == options.Value.Address(SteamEndpoint.Community).Authority ? string.Empty : "[other host]") + string.Join(separator: '/', segments);
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