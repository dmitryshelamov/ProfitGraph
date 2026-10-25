using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Accounts;
using ProfitGraph.Venues.Steam.Confirmations;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     An imported account's Guard status from Steam's own answers against the mock (Profit Fox Designer ADR 0083).
/// </summary>
public sealed class SteamEligibilityTests
{
    private SteamSessionHarness _harness = null!;
    private FakeSteamAccountStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SteamSessionHarness();
        _harness.Clock.SetUtcNow(_harness.Steam.ServerTime);
        _store = new FakeSteamAccountStore { Status = SteamGuardStatus.Checking, NextCheckAt = _harness.Clock.GetUtcNow() };
    }

    [Test]
    public async Task AnAccountThatPassesEveryCheckIsReady()
    {
        (await Check()).ShouldBe(SteamGuardStatus.Ready);
        _harness.Steam.Count("/market/eligibilitycheck/").ShouldBe(1);
        _harness.Steam.Count("/mobileconf/getlist").ShouldBe(1);
    }

    [Test]
    public async Task OnlyAPositiveRefusalRestricts()
    {
        _harness.Steam.EligibilityRedirect = null;

        (await Check()).ShouldBe(SteamGuardStatus.Restricted);
    }

    [Test]
    public async Task RefusedConfirmationsRestrict()
    {
        _harness.Steam.IdentitySecret = "c29tZS1vdGhlci1zZWNyZXQtdmFsdWU=";

        (await Check()).ShouldBe(SteamGuardStatus.Restricted);
    }

    [Test]
    public async Task ARateLimitedCheckStaysUnconfirmed()
    {
        _harness.Steam.Override = request =>
            request.RequestUri!.AbsolutePath == "/market/eligibilitycheck/" ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : null;

        (await Check()).ShouldBe(SteamGuardStatus.Checking);
    }

    [Test]
    public async Task AClockFarFromSteamsStaysUnconfirmed()
    {
        _harness.Steam.ServerTime = _harness.Clock.GetUtcNow().AddMinutes(11);

        (await Check()).ShouldBe(SteamGuardStatus.Checking);
        _harness.Steam.Count("/market/eligibilitycheck/").ShouldBe(0);
    }

    [Test]
    public async Task TheWorkerRecordsTheAttemptThenTheOutcome()
    {
        _harness.Steam.EligibilityRedirect = "/login/home/?goto=market";
        SteamEligibilityWorker worker = Worker();

        await worker.Round(CancellationToken.None);
        await worker.Round(CancellationToken.None);

        _store.Status.ShouldBe(SteamGuardStatus.Checking);
        _store.NextCheckAt.ShouldBe(_harness.Clock.GetUtcNow() + TimeSpan.FromMinutes(15));
        _harness.Steam.Count("/market/eligibilitycheck/").ShouldBe(2);

        _harness.Steam.EligibilityRedirect = "/market/";
        _harness.Clock.Advance(TimeSpan.FromMinutes(15));
        _harness.Steam.ServerTime = _harness.Clock.GetUtcNow();
        await worker.Round(CancellationToken.None);

        _store.Status.ShouldBe(SteamGuardStatus.Ready);
        _store.NextCheckAt.ShouldBeNull();
    }

    [Test]
    public async Task OneAccountThatFailsDoesNotStopTheOthers()
    {
        var broken = Guid.NewGuid();
        DateTimeOffset now = _harness.Clock.GetUtcNow();
        _store.Others[broken] = new SteamAccount(broken, "broken", SteamGuardStatus.Checking, now, now, now);
        _harness.Credentials.Get(new ExchangeCredentialId(broken), Arg.Any<CancellationToken>())
            .Returns(
                ExchangeCredential.Restore(
                    new ExchangeCredentialId(broken),
                    new UserId(SteamSessionHarness.Owner),
                    new VenueId("steam"),
                    new ExchangeCredentialLabel("broken"),
                    new ApiKeyLast4("0002"),
                    new Dictionary<string, EncryptedSecret>
                    {
                        ["steamId"] = new EncryptedSecret("test", new byte[EncryptedSecret.NonceLength], [1], new byte[EncryptedSecret.TagLength])
                    },
                    now));
        _harness.Sender.Send(Arg.Is<ReadExchangeCredentialSecretsQuery>(query => query != null && query.Id == broken), Arg.Any<CancellationToken>())
            .Returns(
                new ExchangeCredentialSecretsInternal(
                    broken,
                    "steam",
                    new Dictionary<string, string>
                    {
                        ["steamId"] = MockSteam.SteamId,
                        ["refreshToken"] = MockSteam.RefreshToken,
                        ["sharedSecret"] = "c2hhcmVkLW5vdC1yZWFs",
                        ["identitySecret"] = "c2hvcnQh",
                        ["deviceId"] = "android:00000000-0000-0000-0000-000000000000"
                    }));

        await Worker().Round(CancellationToken.None);

        _store.Others[broken].GuardStatus.ShouldBe(SteamGuardStatus.Checking);
        _store.Others[broken].NextCheckAt.ShouldBe(now + TimeSpan.FromMinutes(15));
        _store.Status.ShouldBe(SteamGuardStatus.Ready);
        _store.NextCheckAt.ShouldBeNull();
    }

    private SteamEligibility Eligibility()
    {
        IOptions<SteamOptions> options = Options.Create(_harness.Options);

        return new SteamEligibility(_harness.Sessions(), _harness.MarketData(), new SteamConfirmationClient(_harness.Scopes()), options, _harness.Clock);
    }

    private Task<SteamGuardStatus> Check()
    {
        return Eligibility().Check(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
    }

    private SteamEligibilityWorker Worker()
    {
        var scopes = new ServiceCollection()
            .AddSingleton<ISteamAccountStore>(_store)
            .AddSingleton(_harness.Credentials)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new SteamEligibilityWorker(
            scopes,
            Eligibility(),
            Options.Create(_harness.Options),
            _harness.Clock,
            NullLogger<SteamEligibilityWorker>.Instance);
    }
}