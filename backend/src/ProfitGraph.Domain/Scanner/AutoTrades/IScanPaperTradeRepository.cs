using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Scanner.AutoTrades;

public interface IScanPaperTradeRepository
{
    Task Add(ScanPaperTrade trade, CancellationToken cancellationToken);

    /// <summary>
    ///     The venue's paper trades still waiting for their entry or open, locked for a step.
    /// </summary>
    Task<IReadOnlyList<ScanPaperTrade>> ListOpenForUpdate(VenueId venue, CancellationToken cancellationToken);

    /// <summary>
    ///     The rule's paper trades created since <paramref name="since" /> or still open.
    /// </summary>
    Task<IReadOnlyList<ScanPaperTrade>> ListRecent(ScanAutoTradeRuleId ruleId, DateTimeOffset since, CancellationToken cancellationToken);

    Task Update(ScanPaperTrade trade, CancellationToken cancellationToken);
}