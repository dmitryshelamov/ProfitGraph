using System.Net;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Accounts;
using ProfitGraph.Venues.Steam.Protection;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     The Steam Guard code made on request from the stored shared secret for Steam's time.
/// </summary>
public sealed class SteamGuardCodeTests
{
    [Test]
    public void FollowsSteamsAlgorithm()
    {
        DateTimeOffset at = DateTimeOffset.FromUnixTimeSeconds(1792929600);

        SteamGuardCode.For("c2hhcmVkLW5vdC1yZWFs", at).ShouldBe("BWR29");
        SteamGuardCode.For("c2hhcmVkLW5vdC1yZWFs", at.AddSeconds(29)).ShouldBe("BWR29");
        SteamGuardCode.For("c2hhcmVkLW5vdC1yZWFs", at.AddSeconds(30)).ShouldNotBe("BWR29");
    }

    [Test]
    public async Task TheCodeIsForSteamsTimeAndSaysWhenItEnds()
    {
        var harness = new SteamSessionHarness();
        harness.Clock.SetUtcNow(harness.Steam.ServerTime.AddSeconds(-20));
        harness.Steam.ServerTime = harness.Steam.ServerTime.AddSeconds(10);

        AuthenticatorCodeInternal code = await Codes(harness).Current(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        code.Code.ShouldBe("BWR29");
        code.ValidUntil.ShouldBe(harness.Clock.GetUtcNow().AddSeconds(20));
    }

    [Test]
    public async Task WithSteamDisabledThereIsNoCode()
    {
        var harness = new SteamSessionHarness();
        harness.Options = new SteamOptions
        {
            Enabled = false,
            CommunityBaseAddress = MockSteam.Community,
            LoginBaseAddress = MockSteam.Login,
            ApiBaseAddress = MockSteam.Api
        };

        var exception = await Should.ThrowAsync<AuthenticatorCodeUnavailableException>(() =>
            Codes(harness).Current(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None));

        exception.Code.ShouldBe(ExchangeCredentialErrorCodes.AuthenticatorCodeUnavailable);
        exception.Message.ShouldContain("disabled");
        harness.Steam.Count("/ITwoFactorService/QueryTime/v1/").ShouldBe(0);
    }

    [Test]
    public async Task WhileTheProtectionHoldsTheAccountThereIsNoCode()
    {
        var harness = new SteamSessionHarness();
        await harness.Store.Open(SteamSessionHarness.Credential, SteamProtectionReason.Unauthorized, harness.Clock.GetUtcNow(), CancellationToken.None);

        var exception = await Should.ThrowAsync<AuthenticatorCodeUnavailableException>(() =>
            Codes(harness).Current(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None));

        exception.Message.ShouldContain("held back");
        harness.Steam.Count("/ITwoFactorService/QueryTime/v1/").ShouldBe(0);
    }

    [Test]
    public async Task WithoutSteamsTimeThereIsNoCode()
    {
        var harness = new SteamSessionHarness();
        harness.Steam.Override = request => request.RequestUri!.AbsolutePath == "/ITwoFactorService/QueryTime/v1/"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }
            : null;

        var exception = await Should.ThrowAsync<AuthenticatorCodeUnavailableException>(() =>
            Codes(harness).Current(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None));

        exception.Message.ShouldContain("try again later");
    }

    private static SteamAuthenticatorCodes Codes(SteamSessionHarness harness)
    {
        return new SteamAuthenticatorCodes(harness.Sessions(), harness.Scopes(), Options.Create(harness.Options), harness.Clock);
    }
}