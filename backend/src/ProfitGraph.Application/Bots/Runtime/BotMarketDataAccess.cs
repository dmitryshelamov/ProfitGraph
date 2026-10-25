using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     The bot's own credential as market data access for the runtime's venue reads (ADR 0023, TC6), only for a venue that
///     declares <see cref="IVenueMarketDataAccess" />; any other venue is read without it. A deleted credential gives no
///     access. The caller opens it with <see cref="MarketDataAccess.Use" /> in its own method: an ambient value set inside
///     an awaited method does not flow back to its caller.
/// </summary>
public sealed class BotMarketDataAccess(MarketDataCredentials credentials, VenuePorts<IVenueMarketDataAccess> venues)
{
    public async Task<ExchangeCredentialSecretsInternal?> Read(Bot bot, CancellationToken cancellationToken)
    {
        return venues.Find(bot.Series.Venue) is null
            ? null
            : await credentials.ReadIfStillThere(bot.OwnerId.Value, bot.CredentialId.Value, bot.Series.Venue, cancellationToken);
    }
}