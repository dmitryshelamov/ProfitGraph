using System.Net;

namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     The cookies Steam sets on page navigations of one account, such as <c>webTradeEligibility</c> after the market
///     eligibility check (Profit Fox Designer <c>SteamWalletReadCookies</c>). A browser keeps them; without them every market
///     page goes through the eligibility check, which Steam rate limits. They never replace the session's own
///     <c>steamLoginSecure</c> or <c>sessionid</c>, and <see cref="ToString" /> never prints them.
/// </summary>
internal sealed class SteamPageCookies
{
    private readonly CookieContainer _cookies = new CookieContainer();
    private readonly Lock _gate = new Lock();

    public void Merge(Uri address, IEnumerable<string> headers)
    {
        lock (_gate)
        {
            foreach (string header in headers)
            {
                try
                {
                    _cookies.SetCookies(address, header);
                }
                catch (CookieException)
                {
                    // A malformed optional cookie must not end the navigation.
                }
            }
        }
    }

    public string Apply(Uri address, string sessionCookies)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string pair in sessionCookies.Split(separator: ';', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');

            if (separator > 0)
            {
                values[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
            }
        }

        lock (_gate)
        {
            foreach (Cookie cookie in _cookies.GetCookies(address))
            {
                if (!cookie.Name.Equals("steamLoginSecure", StringComparison.OrdinalIgnoreCase) && !cookie.Name.Equals("sessionid", StringComparison.OrdinalIgnoreCase))
                {
                    values[cookie.Name] = cookie.Value;
                }
            }
        }

        return string.Join("; ", values.Select(pair => pair.Key + "=" + pair.Value));
    }

    public override string ToString()
    {
        return "SteamPageCookies { Redacted }";
    }
}