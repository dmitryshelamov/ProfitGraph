using System.Net;
using System.Text;

using NSubstitute;

using ProfitGraph.Application.Bots.RearmBotsForVenueAccount;
using ProfitGraph.Application.Bots.StopBotsForVenueAccount;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Protection;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     The account protection against the mock Steam (Profit Fox Designer ADRs 0083 and 0089, problem 13).
/// </summary>
public sealed class SteamAccountProtectionTests
{
    private static readonly SteamHttpRequest Listings = SteamHttpRequest.Get(SteamEndpoint.Community, "/market/mylistings/");

    [Test]
    public async Task ThreeUnauthorizedAnswersStopTheAccountAndItsBots()
    {
        var harness = new SteamSessionHarness();
        SteamAccountSession session = harness.Session();
        await session.Execute(Listings, CancellationToken.None);
        harness.Steam.Override = request => request.RequestUri!.AbsolutePath == "/market/mylistings/" ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : null;

        await session.Execute(Listings, CancellationToken.None);
        await session.Execute(Listings, CancellationToken.None);
        int sentBefore = harness.Steam.Count("/market/mylistings/");
        SteamRequestResult held = await session.Execute(Listings, CancellationToken.None);

        held.Outcome.ShouldBe(SteamRequestOutcome.NotSent);
        harness.Steam.Count("/market/mylistings/").ShouldBe(sentBefore);
        (await harness.Store.Get(SteamSessionHarness.Credential, CancellationToken.None))!.Value.Reason.ShouldBe(SteamProtectionReason.Unauthorized);
        await harness.Sender.Received(1).Send(new StopBotsForVenueAccountCommand(SteamSessionHarness.Credential), Arg.Any<CancellationToken>());
        (await harness.Protection().Read(SteamSessionHarness.Credential, CancellationToken.None)).AutomaticResumeAt.ShouldBeNull();
    }

    [Test]
    public async Task ACaptchaStopsTheAccountAtOnce()
    {
        var harness = new SteamSessionHarness();
        harness.Steam.Override = request => request.RequestUri!.AbsolutePath == "/market/mylistings/"
            ? new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(content: """{"success":false,"captcha_needed":true}""", Encoding.UTF8, "application/json") }
            : null;

        SteamRequestResult result = await harness.Session().Execute(Listings, CancellationToken.None);

        result.Outcome.ShouldBe(SteamRequestOutcome.Captcha);
        (await harness.Protection().Read(SteamSessionHarness.Credential, CancellationToken.None)).Reason.ShouldBe("Captcha");
    }

    [Test]
    public void ExplicitRejectionsAndSuccessesKeepItClear()
    {
        var counters = new SteamProtectionCounters(new SteamOptions());

        for (var index = 0; index < 20; index++)
        {
            counters.Record(index % 2 == 0 ? SteamRequestOutcome.ClientError : SteamRequestOutcome.Success).ShouldBeNull();
        }

        counters.Record(SteamRequestOutcome.Unauthorized).ShouldBeNull();
        counters.Record(SteamRequestOutcome.Unauthorized).ShouldBeNull();
        counters.Record(SteamRequestOutcome.Success).ShouldBeNull();
        counters.Record(SteamRequestOutcome.Unauthorized).ShouldBeNull();
        counters.Record(SteamRequestOutcome.Deferred).ShouldBeNull();
        counters.Record(SteamRequestOutcome.NotSent).ShouldBeNull();
    }

    [Test]
    public void ScatteredFailuresOpenItByTheirShare()
    {
        var counters = new SteamProtectionCounters(new SteamOptions());
        SteamProtectionReason? reason = null;

        for (var index = 0; index < 10; index++)
        {
            reason = counters.Record(index % 2 == 0 ? SteamRequestOutcome.TransientFailure : SteamRequestOutcome.Success);
        }

        reason.ShouldBe(SteamProtectionReason.ErrorRateExceeded);
    }

    [Test]
    public async Task TransientNoiseResumesByItselfAtMostThreeTimes()
    {
        var harness = new SteamSessionHarness();
        SteamAccountProtection protection = harness.Protection();
        Guid account = SteamSessionHarness.Credential;

        for (var round = 1; round <= 3; round++)
        {
            await Trip(protection, SteamRequestOutcome.TransientFailure);
            harness.Clock.Advance(TimeSpan.FromMinutes(5 * round) - TimeSpan.FromSeconds(1));
            (await protection.Holds(account, CancellationToken.None)).ShouldBeTrue();
            harness.Clock.Advance(TimeSpan.FromSeconds(1));
            (await protection.Holds(account, CancellationToken.None)).ShouldBeFalse();
        }

        await harness.Sender.Received(3).Send(new RearmBotsForVenueAccountCommand(account), Arg.Any<CancellationToken>());
        await Trip(protection, SteamRequestOutcome.TransientFailure);
        (await protection.Read(account, CancellationToken.None)).AutomaticResumeAt.ShouldBeNull();
        harness.Clock.Advance(TimeSpan.FromHours(1));
        (await protection.Holds(account, CancellationToken.None)).ShouldBeTrue();

        await protection.Resume(account, CancellationToken.None);
        (await protection.Holds(account, CancellationToken.None)).ShouldBeFalse();
        await Trip(protection, SteamRequestOutcome.InvalidResponse);
        (await protection.Read(account, CancellationToken.None)).AutomaticResumeAt.ShouldNotBeNull();
    }

    private static async Task Trip(SteamAccountProtection protection, SteamRequestOutcome outcome)
    {
        for (var index = 0; index < 3; index++)
        {
            await protection.Observe(SteamSessionHarness.Credential, outcome, CancellationToken.None);
        }
    }
}