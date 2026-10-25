using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Protection;

/// <summary>
///     The recent answers of one account in this process: refusals of one kind in a row and the failures among the last
///     few answers. A local refusal (not sent, deferred) and an explicit client error count as neither.
/// </summary>
internal sealed class SteamProtectionCounters(SteamOptions options)
{
    private readonly Queue<bool> _recent = new Queue<bool>();
    private SteamRequestOutcome? _streakOutcome;
    private int _streak;

    /// <summary>
    ///     Records an answer and returns the reason to open the protection, if it now must.
    /// </summary>
    public SteamProtectionReason? Record(SteamRequestOutcome outcome)
    {
        switch (outcome)
        {
            case SteamRequestOutcome.Captcha:
                return SteamProtectionReason.Captcha;
            case SteamRequestOutcome.Restricted:
                return SteamProtectionReason.Restricted;
            case SteamRequestOutcome.NotSent or SteamRequestOutcome.Deferred:
                return null;
        }

        bool failure = outcome is not (SteamRequestOutcome.Success or SteamRequestOutcome.ClientError);
        _recent.Enqueue(failure);

        while (_recent.Count > options.ProtectionErrorSample)
        {
            _recent.Dequeue();
        }

        if (!failure || outcome == SteamRequestOutcome.Unknown)
        {
            _streakOutcome = null;
            _streak = 0;
        }
        else
        {
            _streak = _streakOutcome == outcome ? _streak + 1 : 1;
            _streakOutcome = outcome;
        }

        if (_streak >= options.ProtectionThreshold && Streak(outcome) is { } reason)
        {
            return reason;
        }

        return _recent.Count >= options.ProtectionErrorSample && _recent.Count(value => value) >= options.ProtectionErrorRatio * _recent.Count
            ? SteamProtectionReason.ErrorRateExceeded
            : null;
    }

    public void Reset()
    {
        _recent.Clear();
        _streakOutcome = null;
        _streak = 0;
    }

    private static SteamProtectionReason? Streak(SteamRequestOutcome outcome)
    {
        return outcome switch
        {
            SteamRequestOutcome.Unauthorized => SteamProtectionReason.Unauthorized,
            SteamRequestOutcome.Forbidden => SteamProtectionReason.Forbidden,
            SteamRequestOutcome.RateLimited => SteamProtectionReason.RateLimited,
            SteamRequestOutcome.InvalidResponse => SteamProtectionReason.InvalidResponse,
            SteamRequestOutcome.TransientFailure => SteamProtectionReason.TransientFailure,
            _ => null
        };
    }
}