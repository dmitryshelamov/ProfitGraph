using ProfitGraph.Venues.Steam.Orders;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Hands every bot the account a test gives.
/// </summary>
internal sealed class FakeSteamBotAccounts(Func<SteamBotAccount> account) : ISteamBotAccounts
{
    public Task<SteamBotAccount> Get(Guid botId, CancellationToken cancellationToken)
    {
        return Task.FromResult(account());
    }
}