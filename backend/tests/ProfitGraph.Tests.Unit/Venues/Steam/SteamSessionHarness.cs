using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     A session against <see cref="MockSteam" /> with an invented credential and a fake clock.
/// </summary>
internal sealed class SteamSessionHarness
{
    public static readonly Guid Owner = Guid.NewGuid();
    public static readonly Guid Credential = Guid.NewGuid();

    public MockSteam Steam { get; } = new MockSteam();

    public FakeTimeProvider Clock { get; } = new FakeTimeProvider(new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 11, minute: 59, second: 0, TimeSpan.Zero));

    public ISender Sender { get; } = Substitute.For<ISender>();

    public SteamOptions Options { get; set; } = new SteamOptions
    {
        Enabled = true,
        CommunityBaseAddress = MockSteam.Community,
        LoginBaseAddress = MockSteam.Login,
        ApiBaseAddress = MockSteam.Api
    };

    public SteamSessionHarness()
    {
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
        return new SteamHttpTransport(new HttpClient(Steam), Microsoft.Extensions.Options.Options.Create(Options));
    }

    public SteamAccountSession Session()
    {
        ServiceProvider services = new ServiceCollection().AddSingleton(Sender).BuildServiceProvider();

        return new SteamAccountSessions(
                Transport(),
                Scheduler(),
                services.GetRequiredService<IServiceScopeFactory>(),
                Microsoft.Extensions.Options.Options.Create(Options),
                Clock)
            .For(Owner, Credential);
    }
}