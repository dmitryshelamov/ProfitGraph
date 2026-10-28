using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Infrastructure.Accounts.Security;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Accounts;
using ProfitGraph.Venues.Steam.Enrollment;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Linking a new authenticator against the mock (ADR 0022): password sign-in with the emailed code, the recovery code shown
///     once, activation, and every way it stops without linking twice.
/// </summary>
public sealed class SteamEnrollmentTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private SteamSessionHarness _harness = null!;
    private MockSteamEnrollment _steam = null!;
    private InMemorySteamEnrollmentStore _store = null!;
    private FakeSteamAccountStore _accounts = null!;
    private AesGcmSecretProtector _protector = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SteamSessionHarness
        {
            Options = new SteamOptions
            {
                Enabled = true,
                CommunityBaseAddress = MockSteam.Community,
                LoginBaseAddress = MockSteam.Login,
                ApiBaseAddress = MockSteam.Api,
                WriteSpacing = TimeSpan.Zero
            }
        };
        _harness.Clock.SetUtcNow(_harness.Steam.ServerTime);
        _steam = new MockSteamEnrollment { ServerTime = _harness.Steam.ServerTime };
        _harness.Steam.Enrollment = _steam;
        _store = new InMemorySteamEnrollmentStore();
        _accounts = new FakeSteamAccountStore();
        _protector = new AesGcmSecretProtector(
            Options.Create(
                new SecretsOptions
                {
                    ActiveKeyId = "test-v1",
                    MasterKeys = new Dictionary<string, string> { ["test-v1"] = Convert.ToBase64String(Enumerable.Repeat((byte)0x21, count: 32).ToArray()) }
                }),
            NullLogger<AesGcmSecretProtector>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _steam.Dispose();
    }

    [Test]
    public async Task ANewAuthenticatorIsLinkedStepByStep()
    {
        SteamCredentialEnrollment enrollment = Enrollment();
        var id = Guid.NewGuid();

        CredentialEnrollmentStepInternal started = await Start(enrollment, id);
        CredentialEnrollmentStepInternal coded = await enrollment.Continue(Owner, id, "email-code", Code(MockSteamEnrollment.EmailCode), CancellationToken.None);
        CredentialEnrollmentStepInternal saved = await enrollment.Continue(Owner, id, "recovery-saved", new Dictionary<string, string>(), CancellationToken.None);
        CredentialEnrollmentStepInternal activated = await enrollment.Continue(Owner, id, "activate", Code(MockSteamEnrollment.ActivationCode), CancellationToken.None);
        await enrollment.Stored(Owner, id, CancellationToken.None);

        Expect(started, "email-code", ["code"]);
        coded.Step.ShouldBe("recovery-saved");
        coded.OneTimeSecret.ShouldBe(MockSteamEnrollment.RecoveryCode);
        Expect(saved, "activate", ["code"]);
        activated.Step.ShouldBe(CredentialEnrollmentStepInternal.CompletedStep);
        activated.Completed!.PublicHint.ShouldBe(MockSteam.SteamId[^4..]);
        activated.Completed.Fields.ShouldBe(
            new Dictionary<string, string>
            {
                ["steamId"] = MockSteam.SteamId,
                ["refreshToken"] = MockSteam.RefreshToken,
                ["sharedSecret"] = MockSteamEnrollment.SharedSecret,
                ["identitySecret"] = MockSteamEnrollment.IdentitySecret,
                ["deviceId"] = activated.Completed.Fields["deviceId"]
            },
            ignoreOrder: true);
        activated.Completed.Fields["deviceId"].ShouldStartWith("android:");
        _accounts.Added.Single()
            .ShouldSatisfyAllConditions(
                account => account.CredentialId.ShouldBe(id),
                account => account.GuardStatus.ShouldBe(SteamGuardStatus.CoolingDown),
                account => account.NextCheckAt.ShouldBe(_harness.Clock.GetUtcNow() + TimeSpan.FromDays(15)));
        _store.All.ShouldBeEmpty();
        (_steam.Adds, _steam.Finalizes).ShouldBe((1, 1));
    }

    [Test]
    public async Task NeitherThePasswordNorTheRecoveryCodeIsStored()
    {
        SteamCredentialEnrollment enrollment = Enrollment();
        var id = Guid.NewGuid();
        await Start(enrollment, id);
        await enrollment.Continue(Owner, id, "email-code", Code(MockSteamEnrollment.EmailCode), CancellationToken.None);

        SteamPendingEnrollment pending = _store.All.Single();
        string state = _protector.Decrypt(new ExchangeCredentialId(id), new UserId(Owner), "steamEnrollment", pending.Payload);

        state.ShouldNotContain(MockSteamEnrollment.Password);
        state.ShouldNotContain(MockSteamEnrollment.RecoveryCode);
        Encoding.UTF8.GetString(pending.Payload.Ciphertext.Span).ShouldNotContain(MockSteam.SteamId);
        pending.ToString().ShouldNotContain(MockSteam.SteamId);
    }

    [Test]
    public async Task AWrongCodeIsTypedAgainWithoutLinkingAgain()
    {
        SteamCredentialEnrollment enrollment = Enrollment();
        var id = Guid.NewGuid();
        await Start(enrollment, id);

        Expect(await enrollment.Continue(Owner, id, "email-code", Code("WRONG"), CancellationToken.None), "email-code", ["code"], "invalid_code");
        await enrollment.Continue(Owner, id, "email-code", Code(MockSteamEnrollment.EmailCode), CancellationToken.None);
        await enrollment.Continue(Owner, id, "recovery-saved", new Dictionary<string, string>(), CancellationToken.None);
        (await enrollment.Continue(Owner, id, "activate", Code("WRONG"), CancellationToken.None)).Problem.ShouldBe("invalid_code");
        (await enrollment.Continue(Owner, id, "activate", Code(MockSteamEnrollment.ActivationCode), CancellationToken.None)).Step.ShouldBe("completed");

        (_steam.Adds, _steam.Finalizes).ShouldBe((1, 2));
    }

    [Test]
    public async Task AnActivationSteamKeepsAskingAboutWaitsForAnotherTry()
    {
        _steam.WantMore = true;
        SteamCredentialEnrollment enrollment = Enrollment();
        var id = Guid.NewGuid();
        await Start(enrollment, id);
        await enrollment.Continue(Owner, id, "email-code", Code(MockSteamEnrollment.EmailCode), CancellationToken.None);
        await enrollment.Continue(Owner, id, "recovery-saved", new Dictionary<string, string>(), CancellationToken.None);

        Expect(
            await enrollment.Continue(Owner, id, "activate", Code(MockSteamEnrollment.ActivationCode), CancellationToken.None),
            "activate",
            ["code"],
            "activation_pending");
        _steam.Finalizes.ShouldBe(3);
        _steam.WantMore = false;
        (await enrollment.Continue(Owner, id, "activate", Code(MockSteamEnrollment.ActivationCode), CancellationToken.None)).Step.ShouldBe("completed");
    }

    [TestCase(arg1: 2, "phone_required")]
    [TestCase(arg1: 29, "authenticator_exists")]
    public async Task SteamsRefusalStopsWithItsReason(int status, string problem)
    {
        _steam.AddStatus = status;
        SteamCredentialEnrollment enrollment = Enrollment();
        var id = Guid.NewGuid();
        await Start(enrollment, id);

        CredentialEnrollmentStepInternal stopped = await enrollment.Continue(Owner, id, "email-code", Code(MockSteamEnrollment.EmailCode), CancellationToken.None);

        Expect(stopped, "stopped", [], problem);
        _store.All.ShouldBeEmpty();
    }

    [Test]
    public async Task AnAccountWithAnAuthenticatorOrAWrongPasswordNeverReachesTheTwoFactorService()
    {
        _steam.Confirmation = 3;

        (await Start(Enrollment(), Guid.NewGuid())).Problem.ShouldBe("authenticator_exists");
        (await Enrollment().Start(Owner, Guid.NewGuid(), "Main", Fields(password: "wrong"), CancellationToken.None)).Problem.ShouldBe("invalid_login");
        _steam.Adds.ShouldBe(0);
        _store.All.ShouldBeEmpty();
    }

    [Test]
    public async Task APendingEnrollmentSurvivesARestartUntilItExpires()
    {
        var id = Guid.NewGuid();
        await Start(Enrollment(), id);
        SteamCredentialEnrollment restarted = Enrollment();

        (await restarted.Continue(Owner, id, "email-code", Code(MockSteamEnrollment.EmailCode), CancellationToken.None)).Step.ShouldBe("recovery-saved");
        _harness.Clock.Advance(TimeSpan.FromMinutes(11));

        (await restarted.Continue(Owner, id, "recovery-saved", new Dictionary<string, string>(), CancellationToken.None)).Problem.ShouldBe("enrollment_expired");
        _store.All.ShouldBeEmpty();
    }

    [Test]
    public async Task AnUnclearAddIsNeverSentAgain()
    {
        SteamCredentialEnrollment enrollment = Enrollment();
        var id = Guid.NewGuid();
        await Start(enrollment, id);
        _harness.Steam.Override = request => request.RequestUri!.AbsolutePath == "/ITwoFactorService/AddAuthenticator/v1/"
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : null;

        (await enrollment.Continue(Owner, id, "email-code", Code(MockSteamEnrollment.EmailCode), CancellationToken.None)).Problem.ShouldBe("enrollment_unknown");
        _harness.Steam.Override = null;
        (await enrollment.Continue(Owner, id, "sign-in-pending", new Dictionary<string, string>(), CancellationToken.None)).Problem.ShouldBe("enrollment_unknown");

        _harness.Steam.Count("/ITwoFactorService/AddAuthenticator/v1/").ShouldBe(1);
    }

    [Test]
    public async Task AnotherOwnerCannotContinueIt()
    {
        var id = Guid.NewGuid();
        await Start(Enrollment(), id);

        (await Enrollment().Continue(Guid.NewGuid(), id, "email-code", Code(MockSteamEnrollment.EmailCode), CancellationToken.None)).Problem.ShouldBe(
            "enrollment_not_found");
    }

    private static void Expect(CredentialEnrollmentStepInternal step, string name, string[] fields, string? problem = null)
    {
        step.ShouldSatisfyAllConditions(
            () => step.Step.ShouldBe(name),
            () => step.Fields.ShouldBe(fields),
            () => step.Problem.ShouldBe(problem),
            () => step.OneTimeSecret.ShouldBeNull());
    }

    private static Dictionary<string, string> Code(string code)
    {
        return new Dictionary<string, string> { ["code"] = code };
    }

    private static Dictionary<string, string> Fields(string password = MockSteamEnrollment.Password)
    {
        return new Dictionary<string, string> { ["accountName"] = MockSteamEnrollment.AccountName, ["password"] = password };
    }

    private static Task<CredentialEnrollmentStepInternal> Start(SteamCredentialEnrollment enrollment, Guid id)
    {
        return enrollment.Start(Owner, id, "Main", Fields(), CancellationToken.None);
    }

    private SteamCredentialEnrollment Enrollment()
    {
        IOptions<SteamOptions> options = Options.Create(_harness.Options);
        var scopes = new ServiceCollection().AddSingleton<ISteamAccountStore>(_accounts).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new SteamCredentialEnrollment(_store, new SteamEnrollmentClient(_harness.Transport(), _harness.Scheduler()), _protector, scopes, options, _harness.Clock);
    }
}