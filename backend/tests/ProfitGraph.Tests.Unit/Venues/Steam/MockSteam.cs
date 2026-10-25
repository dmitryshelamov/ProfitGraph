using System.Net;
using System.Text;
using System.Text.Json;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     An in-process Steam with one invented account (Profit Fox Designer's mock contract, after the fixes that aligned it
///     with real responses). Hosts end in <c>.invalid</c>, so no request can leave the process. It counts every request and
///     can be told to answer differently.
/// </summary>
internal sealed class MockSteam : HttpMessageHandler
{
    public const string Community = "https://community.steam.invalid/";
    public const string Login = "https://login.steam.invalid/";
    public const string Api = "https://api.steam.invalid/";
    public const string SteamId = "76561190000000001";
    public const string RefreshToken = "refresh-token-not-real";

    private readonly List<(HttpMethod Method, string Path)> _requests = new List<(HttpMethod Method, string Path)>();
    private int _issued;

    public DateTimeOffset ServerTime { get; set; } = new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    /// <summary>
    ///     The cookie value Community accepts; signing in issues a new one.
    /// </summary>
    public string? ValidSession { get; private set; }

    public string ReturnedSteamId { get; set; } = SteamId;

    public long? TokenExpiry { get; set; }

    /// <summary>
    ///     Answers a request instead of the default handling when it returns a response.
    /// </summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }

    public IReadOnlyList<string> Cookies { get; private set; } = [];

    public int Count(string path)
    {
        lock (_requests)
        {
            return _requests.Count(request => request.Path == path);
        }
    }

    public void ExpireSessions()
    {
        ValidSession = null;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;

        lock (_requests)
        {
            _requests.Add((request.Method, path));
            Cookies = request.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookies) ? [.. cookies] : [];
        }

        if (Override?.Invoke(request) is { } answer)
        {
            return answer;
        }

        string form = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

        return (request.RequestUri.Host, path) switch
        {
            ("login.steam.invalid", "/jwt/finalizelogin") when form.Contains(RefreshToken, StringComparison.Ordinal) => Json(
                $$$"""{"steamID":"{{{ReturnedSteamId}}}","transfer_info":[{"url":"https://help.steam.invalid/login/settoken","params":{"nonce":"x"}},{"url":"{{{Community}}}login/settoken","params":{"nonce":"n1","auth":"a1"}}]}"""),
            ("login.steam.invalid", "/jwt/finalizelogin") => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            ("community.steam.invalid", "/login/settoken") => SetToken(),
            ("api.steam.invalid", "/ITwoFactorService/QueryTime/v1/") => Json($$$"""{"response":{"server_time":"{{{ServerTime.ToUnixTimeSeconds()}}}"}}"""),
            ("community.steam.invalid", "/market/") => Market(request),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private HttpResponseMessage SetToken()
    {
        int number = Interlocked.Increment(ref _issued);
        string payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { exp = TokenExpiry ?? ServerTime.AddDays(1).ToUnixTimeSeconds() }))
            .TrimEnd('=')
            .Replace(oldChar: '+', newChar: '-')
            .Replace(oldChar: '/', newChar: '_');
        ValidSession = Uri.EscapeDataString($"{SteamId}||header.{payload}.signature{number}");
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("Set-Cookie", $"steamLoginSecure={ValidSession}; Path=/; Secure; HttpOnly");
        response.Headers.Add("Set-Cookie", "steamCountry=XX%7Cinvented; Path=/");

        return response;
    }

    private HttpResponseMessage Market(HttpRequestMessage request)
    {
        bool signedIn = ValidSession is not null && Cookies.Any(cookie => cookie.Contains($"steamLoginSecure={ValidSession}", StringComparison.Ordinal));

        if (signedIn)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>market</html>", Encoding.UTF8, "text/html") };
        }

        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri("/login/home/?goto=market%2F", UriKind.Relative);

        return redirect;
    }
}