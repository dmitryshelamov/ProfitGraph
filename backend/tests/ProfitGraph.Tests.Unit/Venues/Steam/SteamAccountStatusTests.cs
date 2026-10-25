using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Venues.Steam.Accounts;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     A Steam account's status is its Guard state and when it is checked next.
/// </summary>
public sealed class SteamAccountStatusTests
{
    [Test]
    public async Task TellsTheGuardStateAndTheNextCheck()
    {
        var store = new FakeSteamAccountStore { Status = SteamGuardStatus.CoolingDown, NextCheckAt = DateTimeOffset.UnixEpoch.AddDays(15) };
        var scopes = new ServiceCollection().AddSingleton<ISteamAccountStore>(store).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        VenueAccountStatusInternal? status = await new SteamAccountStatus(scopes).Read(Guid.NewGuid(), CancellationToken.None);

        status.ShouldBe(new VenueAccountStatusInternal("CoolingDown", DateTimeOffset.UnixEpoch.AddDays(15)));
    }
}