using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     Opens the bot's own credential as market data access around the runtime's venue reads (ADR 0023, TC6), only for a
///     venue that declares <see cref="IVenueMarketDataAccess" />; any other venue is read without it. A deleted credential
///     gives no access.
/// </summary>
public sealed class BotMarketDataAccess(MarketDataCredentials credentials, VenuePorts<IVenueMarketDataAccess> venues)
{
    public async Task<IDisposable> Open(Bot bot, CancellationToken cancellationToken)
    {
        return MarketDataAccess.Use(
            venues.Find(bot.Series.Venue) is null
                ? null
                : await credentials.ReadIfStillThere(bot.OwnerId.Value, bot.CredentialId.Value, bot.Series.Venue, cancellationToken));
    }
}