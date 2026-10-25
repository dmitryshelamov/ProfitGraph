using System.Net;
using System.Net.Http.Headers;
using System.Text;

using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     How the transport classifies Steam's answers (Profit Fox Designer ADR 0081, response evidence table).
/// </summary>
public sealed class SteamHttpTransportTests
{
    private static readonly SteamHttpRequest Read = SteamHttpRequest.Get(SteamEndpoint.Community, "/market/mylistings/");

    private static readonly SteamHttpRequest Write = SteamHttpRequest.Write(
        SteamEndpoint.Community,
        "/market/createbuyorder/",
        new Dictionary<string, string> { ["q"] = "1" });

    [TestCase(HttpStatusCode.InternalServerError, arg2: true, "TransientFailure")]
    [TestCase(HttpStatusCode.InternalServerError, arg2: false, "Unknown")]
    [TestCase(HttpStatusCode.Forbidden, arg2: true, "Forbidden")]
    [TestCase(HttpStatusCode.Unauthorized, arg2: false, "Unauthorized")]
    [TestCase(HttpStatusCode.NotAcceptable, arg2: false, "ClientError")]
    public async Task ClassifiesStatusCodesByReadOrWrite(HttpStatusCode status, bool isRead, string expected)
    {
        SteamRequestResult result = await Send(isRead ? Read : Write, _ => new HttpResponseMessage(status));

        result.Outcome.ToString().ShouldBe(expected);
    }

    [Test]
    public async Task ARateLimitKeepsItsRetryAfter()
    {
        SteamRequestResult result = await Send(
            Read,
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));

                return response;
            });

        result.Outcome.ShouldBe(SteamRequestOutcome.RateLimited);
        result.RetryAfter.ShouldBe(TimeSpan.FromSeconds(90));
    }

    [Test]
    public async Task ARedirectIsNeverFollowed()
    {
        var harness = new SteamSessionHarness();

        SteamRequestResult login = await harness.Transport().Send(SteamHttpRequest.Get(SteamEndpoint.Community, "/market/"), cookies: null, CancellationToken.None);
        SteamRequestResult elsewhere = await Send(
            Read,
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://example.invalid/");

                return response;
            });

        login.Outcome.ShouldBe(SteamRequestOutcome.Unauthorized);
        harness.Steam.Count("/login/home/").ShouldBe(0);
        elsewhere.Outcome.ShouldBe(SteamRequestOutcome.InvalidResponse);
    }

    [Test]
    public async Task NeedAuthInTheBodyIsUnauthorized()
    {
        SteamRequestResult result = await Send(Read, _ => Json("""{"success":false,"needauth":true}"""));

        result.Outcome.ShouldBe(SteamRequestOutcome.Unauthorized);
    }

    [Test]
    public async Task AByteOrderMarkIsRemoved()
    {
        SteamRequestResult result = await Send(
            Read,
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}']) });

        result.Outcome.ShouldBe(SteamRequestOutcome.Success);
        Encoding.UTF8.GetString(result.Response!.Body).ShouldBe("{}");
    }

    [Test]
    public async Task AnOversizedBodyIsAnInvalidReadAndAnUnknownWrite()
    {
        Func<HttpRequestMessage, HttpResponseMessage> large = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[4096]) };
        var options = new SteamOptions
        {
            Enabled = true,
            CommunityBaseAddress = MockSteam.Community,
            LoginBaseAddress = MockSteam.Login,
            ApiBaseAddress = MockSteam.Api,
            MaximumResponseBytes = 1024
        };

        (await Send(Read, large, options)).Outcome.ShouldBe(SteamRequestOutcome.InvalidResponse);
        (await Send(Write, large, options)).Outcome.ShouldBe(SteamRequestOutcome.Unknown);
    }

    [Test]
    public async Task ANetworkErrorIsTransientForAReadAndUnknownForAWrite()
    {
        Func<HttpRequestMessage, HttpResponseMessage> failure = _ => throw new HttpRequestException("connection reset");

        (await Send(Read, failure)).Outcome.ShouldBe(SteamRequestOutcome.TransientFailure);
        (await Send(Write, failure)).Outcome.ShouldBe(SteamRequestOutcome.Unknown);
    }

    [Test]
    public async Task APathCannotLeaveItsHost()
    {
        var harness = new SteamSessionHarness();

        await Should.ThrowAsync<ArgumentException>(() =>
            harness.Transport().Send(SteamHttpRequest.Get(SteamEndpoint.Community, "//example.invalid/"), cookies: null, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() =>
            harness.Transport().Send(SteamHttpRequest.Get(SteamEndpoint.Community, "market/"), cookies: null, CancellationToken.None));
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static Task<SteamRequestResult> Send(SteamHttpRequest request, Func<HttpRequestMessage, HttpResponseMessage> answer, SteamOptions? options = null)
    {
        var harness = new SteamSessionHarness();
        harness.Steam.Override = answer;

        if (options is not null)
        {
            harness.Options = options;
        }

        return harness.Transport().Send(request, cookies: null, CancellationToken.None);
    }
}