using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;

using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     The account session against the mock Steam (ADR 0022; Profit Fox Designer ADR 0083 problem 1).
/// </summary>
public sealed class SteamAccountSessionTests
{
    private static readonly SteamHttpRequest Market = SteamHttpRequest.Get(SteamEndpoint.Community, "/market/");

    [Test]
    public async Task ConcurrentCallersShareOneSignIn()
    {
        var harness = new SteamSessionHarness();
        SteamAccountSession session = harness.Session();

        SteamRequestResult[] results = await Task.WhenAll(Enumerable.Range(start: 0, count: 8).Select(_ => session.Execute(Market, CancellationToken.None)));

        results.ShouldAllBe(result => result.Outcome == SteamRequestOutcome.Success);
        harness.Steam.Count("/jwt/finalizelogin").ShouldBe(1);
        harness.Steam.Count("/login/settoken").ShouldBe(1);
        harness.Steam.Cookies.Single().ShouldContain("sessionid=");
        harness.Steam.Cookies.Single().ShouldContain("steamCountry=");
    }

    [Test]
    public async Task AnExpiredReadRenewsOnceAndIsSentOnceMore()
    {
        var harness = new SteamSessionHarness();
        SteamAccountSession session = harness.Session();
        await session.Execute(Market, CancellationToken.None);
        harness.Steam.ExpireSessions();

        SteamRequestResult result = await session.Execute(Market, CancellationToken.None);

        result.Outcome.ShouldBe(SteamRequestOutcome.Success);
        harness.Steam.Count("/jwt/finalizelogin").ShouldBe(2);
        harness.Steam.Count("/market/").ShouldBe(3);
    }

    [Test]
    public async Task AReadThatStaysUnauthorizedIsNotRepeatedAgain()
    {
        var harness = new SteamSessionHarness();
        SteamAccountSession session = harness.Session();
        harness.Steam.Override = request => request.RequestUri!.AbsolutePath == "/market/" ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : null;

        SteamRequestResult result = await session.Execute(Market, CancellationToken.None);

        result.Outcome.ShouldBe(SteamRequestOutcome.Unauthorized);
        harness.Steam.Count("/market/").ShouldBe(2);
    }

    [Test]
    public async Task AWriteIsNeverSentTwice()
    {
        var harness = new SteamSessionHarness();
        SteamAccountSession session = harness.Session();
        harness.Steam.Override = request => request.RequestUri!.AbsolutePath == "/market/createbuyorder/"
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : null;

        SteamRequestResult result = await session.Execute(
            SteamHttpRequest.Write(SteamEndpoint.Community, "/market/createbuyorder/", new Dictionary<string, string> { ["quantity"] = "1" }),
            CancellationToken.None);

        result.Outcome.ShouldBe(SteamRequestOutcome.Unauthorized);
        harness.Steam.Count("/market/createbuyorder/").ShouldBe(1);
    }

    [Test]
    public async Task ASessionIsRenewedBeforeItsTokenExpires()
    {
        var harness = new SteamSessionHarness();
        harness.Steam.TokenExpiry = harness.Clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds();
        SteamAccountSession session = harness.Session();
        await session.Execute(Market, CancellationToken.None);

        harness.Clock.Advance(TimeSpan.FromMinutes(3));
        await session.Execute(Market, CancellationToken.None);
        harness.Steam.Count("/jwt/finalizelogin").ShouldBe(1);

        harness.Clock.Advance(TimeSpan.FromMinutes(1.5));
        await session.Execute(Market, CancellationToken.None);
        harness.Steam.Count("/jwt/finalizelogin").ShouldBe(2);
    }

    [Test]
    public async Task ASignInOfAnotherAccountIsRefused()
    {
        var harness = new SteamSessionHarness();
        harness.Steam.ReturnedSteamId = "76561190000000002";

        SteamRequestResult result = await harness.Session().Execute(Market, CancellationToken.None);

        result.Outcome.ShouldBe(SteamRequestOutcome.InvalidResponse);
        harness.Steam.Count("/login/settoken").ShouldBe(0);
        harness.Steam.Count("/market/").ShouldBe(0);
    }

    [Test]
    public async Task ARefusedRefreshTokenSendsNothingElse()
    {
        var harness = new SteamSessionHarness();
        harness.Steam.Override = request => request.RequestUri!.AbsolutePath == "/jwt/finalizelogin" ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : null;

        SteamRequestResult result = await harness.Session().Execute(Market, CancellationToken.None);

        result.Outcome.ShouldBe(SteamRequestOutcome.Unauthorized);
        harness.Steam.Count("/market/").ShouldBe(0);
    }

    [Test]
    public async Task ServerTimeIsMeasuredOnceAnHour()
    {
        var harness = new SteamSessionHarness();
        harness.Steam.ServerTime = harness.Clock.GetUtcNow().AddSeconds(42);
        SteamAccountSession session = harness.Session();

        (await session.ServerTime(CancellationToken.None)).ShouldBe(harness.Clock.GetUtcNow().AddSeconds(42));
        harness.Clock.Advance(TimeSpan.FromMinutes(59));
        await session.ServerTime(CancellationToken.None);
        harness.Steam.Count("/ITwoFactorService/QueryTime/v1/").ShouldBe(1);
        harness.Clock.Advance(TimeSpan.FromMinutes(2));
        await session.ServerTime(CancellationToken.None);
        harness.Steam.Count("/ITwoFactorService/QueryTime/v1/").ShouldBe(2);
    }

    [Test]
    public async Task ADisabledVenueSendsNothing()
    {
        var harness = new SteamSessionHarness { Options = new SteamOptions() };

        SteamRequestResult result = await harness.Session().Execute(Market, CancellationToken.None);

        result.Outcome.ShouldBe(SteamRequestOutcome.NotSent);
        harness.Steam.Count("/jwt/finalizelogin").ShouldBe(0);
    }

    [Test]
    public void ASessionBelongsToItsOwner()
    {
        var harness = new SteamSessionHarness();
        var sessions = new SteamAccountSessions(
            harness.Transport(),
            harness.Scheduler(),
            harness.Protection(),
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(harness.Options),
            harness.Clock);
        sessions.For(SteamSessionHarness.Owner, SteamSessionHarness.Credential);

        Should.Throw<InvalidOperationException>(() => sessions.For(Guid.NewGuid(), SteamSessionHarness.Credential));
    }
}