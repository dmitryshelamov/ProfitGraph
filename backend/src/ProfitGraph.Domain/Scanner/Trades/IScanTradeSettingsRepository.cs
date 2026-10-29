using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Scanner.Trades;

public interface IScanTradeSettingsRepository
{
    Task<ScanTradeSettings?> Get(UserId ownerId, VenueId venue, CancellationToken cancellationToken);

    /// <summary>
    ///     Inserts the settings or replaces the stored ones of the same owner and venue.
    /// </summary>
    Task Save(ScanTradeSettings settings, CancellationToken cancellationToken);
}