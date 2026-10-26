using Microsoft.Extensions.Options;

namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     What this process last learned about one account's market eligibility (Profit Fox Designer
///     <c>SteamWalletReadCache</c>). Steam rate limits <c>/market/eligibilitycheck/</c>, so a positive answer, from the check
///     or from a market page that went through it, is reused for <see cref="SteamOptions.EligibilityReuse" />, and after a
///     throttled or failed check the next one waits <see cref="SteamOptions.EligibilityBackoff" />. A refusal or an unknown
///     answer is never reused.
/// </summary>
internal sealed class SteamEligibilityEvidence(IOptions<SteamOptions> options, TimeProvider clock)
{
    private readonly Lock _gate = new Lock();
    private DateTimeOffset? _eligibleAt;
    private DateTimeOffset? _backoffUntil;

    public bool RecentlyEligible
    {
        get
        {
            lock (_gate)
            {
                return _eligibleAt is { } at && clock.GetUtcNow() - at < options.Value.EligibilityReuse;
            }
        }
    }

    public bool BackingOff
    {
        get
        {
            lock (_gate)
            {
                return _backoffUntil is { } until && clock.GetUtcNow() < until;
            }
        }
    }

    public void Record(bool eligible)
    {
        lock (_gate)
        {
            _backoffUntil = null;
            _eligibleAt = eligible ? clock.GetUtcNow() : null;
        }
    }

    public void RecordThrottled()
    {
        lock (_gate)
        {
            _backoffUntil = clock.GetUtcNow() + options.Value.EligibilityBackoff;
        }
    }
}