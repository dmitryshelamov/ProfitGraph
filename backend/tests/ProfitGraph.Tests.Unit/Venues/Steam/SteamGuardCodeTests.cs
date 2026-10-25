using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Venues.Steam.Accounts;

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

        AuthenticatorCodeInternal code = await new SteamAuthenticatorCodes(harness.Sessions(), harness.Scopes(), harness.Clock)
            .Current(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        code.Code.ShouldBe("BWR29");
        code.ValidUntil.ShouldBe(harness.Clock.GetUtcNow().AddSeconds(20));
    }
}