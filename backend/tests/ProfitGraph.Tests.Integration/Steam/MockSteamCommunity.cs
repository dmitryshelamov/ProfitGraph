using System.Globalization;
using System.Net;
using System.Text;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     A minimal in-process Steam for end-to-end checks: sign-in with the imported refresh token, server time, a KZT market
///     page and the daily median history it is given. Hosts end in <c>.invalid</c>, so nothing leaves the process.
/// </summary>
internal sealed class MockSteamCommunity(string steamId, string refreshToken, IReadOnlyList<(DateTimeOffset Day, decimal Median, int Sold)> history) : HttpMessageHandler
{
    public const string Community = "https://community.steam.invalid/";
    public const string Login = "https://login.steam.invalid/";
    public const string Api = "https://api.steam.invalid/";

    private int _historyReads;

    public int HistoryReads => Volatile.Read(ref _historyReads);

    public static IReadOnlyDictionary<string, string?> Settings(Guid marketDataCredential)
    {
        return new Dictionary<string, string?>
        {
            ["Venues:Steam:Enabled"] = "true",
            ["Venues:Steam:CommunityBaseAddress"] = Community,
            ["Venues:Steam:LoginBaseAddress"] = Login,
            ["Venues:Steam:ApiBaseAddress"] = Api,
            ["Venues:Steam:MarketDataCredentialId"] = marketDataCredential.ToString()
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string form = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

        return (request.RequestUri!.Host, request.RequestUri.AbsolutePath) switch
        {
            ("login.steam.invalid", "/jwt/finalizelogin") when form.Contains(refreshToken, StringComparison.Ordinal) => Json(
                $$$"""{"steamID":"{{{steamId}}}","transfer_info":[{"url":"{{{Community}}}login/settoken","params":{"nonce":"n"}}]}"""),
            ("community.steam.invalid", "/login/settoken") => SetToken(),
            ("api.steam.invalid", "/ITwoFactorService/QueryTime/v1/") => Json($$$"""{"response":{"server_time":"{{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}"}}"""),
            ("community.steam.invalid", "/market/") => Html(
                """<script>var g_rgWalletInfo = {"wallet_currency":37,"wallet_fee_percent":"0.05","wallet_publisher_fee_percent_default":"0.10","wallet_balance":"10000000","wallet_market_minimum":1,"wallet_currency_increment":1};</script>"""),
            ("community.steam.invalid", "/market/pricehistory/") => History(),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static HttpResponseMessage Html(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") };
    }

    private HttpResponseMessage SetToken()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("Set-Cookie", $"steamLoginSecure={Uri.EscapeDataString(steamId + "||not.a.jwt")}; Path=/");

        return response;
    }

    private HttpResponseMessage History()
    {
        Interlocked.Increment(ref _historyReads);
        string points = string.Join(
            separator: ',',
            history.Select(point => string.Create(
                CultureInfo.InvariantCulture,
                $"[\"{point.Day.AddHours(1).ToString("MMM dd yyyy HH", CultureInfo.InvariantCulture)}: +0\",{point.Median},\"{point.Sold}\"]")));

        return Json("""{"success":true,"price_prefix":"","price_suffix":"₸","prices":[""" + points + "]}");
    }
}