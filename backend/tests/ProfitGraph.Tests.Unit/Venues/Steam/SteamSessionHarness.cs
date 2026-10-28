using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.MarketData;
using ProfitGraph.Venues.Steam.Protection;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     A session against <see cref="MockSteam" /> with an invented credential and a fake clock.
/// </summary>
internal sealed class SteamSessionHarness
{
    public static readonly Guid Owner = Guid.NewGuid();
    public static readonly Guid Credential = Guid.NewGuid();

    private SteamAccountProtection? _protection;
    private SteamAccountSessions? _sessions;

    public MockSteam Steam { get; } = new MockSteam();

    public FakeTimeProvider Clock { get; } = new FakeTimeProvider(new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 11, minute: 59, second: 0, TimeSpan.Zero));

    public ISender Sender { get; } = Substitute.For<ISender>();

    public IExchangeCredentialRepository Credentials { get; } = Substitute.For<IExchangeCredentialRepository>();

    public SteamOptions Options { get; set; } = new SteamOptions
    {
        Enabled = true,
        CommunityBaseAddress = MockSteam.Community,
        LoginBaseAddress = MockSteam.Login,
        ApiBaseAddress = MockSteam.Api,
        MarketDataCredentialId = Credential
    };

    public InMemorySteamProtectionStore Store { get; } = new InMemorySteamProtectionStore();

    public SteamSessionHarness()
    {
        Credentials.Get(new ExchangeCredentialId(Credential), Arg.Any<CancellationToken>())
            .Returns(
                ExchangeCredential.Restore(
                    new ExchangeCredentialId(Credential),
                    new UserId(Owner),
                    new VenueId("steam"),
                    new ExchangeCredentialLabel("market data"),
                    new ApiKeyLast4("0001"),
                    new Dictionary<string, EncryptedSecret>
                    {
                        ["steamId"] = new EncryptedSecret("test", new byte[EncryptedSecret.NonceLength], [1], new byte[EncryptedSecret.TagLength])
                    },
                    Clock.GetUtcNow()));
        Sender.Send(Arg.Any<ReadExchangeCredentialSecretsQuery>(), Arg.Any<CancellationToken>())
            .Returns(
                new ExchangeCredentialSecretsInternal(
                    Credential,
                    "steam",
                    new Dictionary<string, string>
                    {
                        ["steamId"] = MockSteam.SteamId,
                        ["refreshToken"] = MockSteam.RefreshToken,
                        ["sharedSecret"] = "c2hhcmVkLW5vdC1yZWFs",
                        ["identitySecret"] = "aWRlbnRpdHktbm90LXJlYWw=",
                        ["deviceId"] = "android:00000000-0000-0000-0000-000000000000"
                    }));
    }

    public SteamRequestScheduler Scheduler()
    {
        return new SteamRequestScheduler(Microsoft.Extensions.Options.Options.Create(Options), Clock);
    }

    public SteamHttpTransport Transport()
    {
        return new SteamHttpTransport(
            new HttpClient(Steam),
            Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<SteamHttpTransport>.Instance);
    }

    public SteamAccountProtection Protection()
    {
        return _protection ??= new SteamAccountProtection(Store, Scopes(), Microsoft.Extensions.Options.Options.Create(Options), Clock);
    }

    public IServiceScopeFactory Scopes()
    {
        return new ServiceCollection().AddSingleton(Sender).AddSingleton(Credentials).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    public SteamAccountSessions Sessions()
    {
        return _sessions ??= new SteamAccountSessions(
            Transport(),
            Scheduler(),
            Protection(),
            Scopes(),
            Microsoft.Extensions.Options.Options.Create(Options),
            Clock);
    }

    public SteamMarketDataAccount MarketData()
    {
        return new SteamMarketDataAccount(Sessions(), Scopes(), Microsoft.Extensions.Options.Options.Create(Options), Clock);
    }

    public SteamAccountSession Session()
    {
        return new SteamAccountSessions(
                Transport(),
                Scheduler(),
                Protection(),
                Scopes(),
                Microsoft.Extensions.Options.Options.Create(Options),
                Clock)
            .For(Owner, Credential);
    }
}