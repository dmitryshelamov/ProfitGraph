using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Scanner.AutoTrades;

public interface IScanAutoTradeRuleRepository
{
    Task<ScanAutoTradeRule?> Get(ScanPresetId presetId, UserId ownerId, CancellationToken cancellationToken);

    Task<ScanAutoTradeRule?> GetForUpdate(ScanPresetId presetId, UserId ownerId, CancellationToken cancellationToken);

    Task<ScanAutoTradeRule?> GetForUpdate(ScanAutoTradeRuleId id, CancellationToken cancellationToken);

    /// <summary>
    ///     The rules of the venue's presets that are on, locked for the snapshot check.
    /// </summary>
    Task<IReadOnlyList<ScanAutoTradeRule>> ListOnForUpdate(VenueId venue, CancellationToken cancellationToken);

    Task Add(ScanAutoTradeRule rule, CancellationToken cancellationToken);

    Task Update(ScanAutoTradeRule rule, CancellationToken cancellationToken);

    Task Delete(ScanAutoTradeRuleId id, CancellationToken cancellationToken);
}