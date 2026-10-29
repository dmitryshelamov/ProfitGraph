using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Scanner.Trades;

public interface IScanTradeRepository
{
    Task Add(ScanTrade trade, CancellationToken cancellationToken);

    Task Update(ScanTrade trade, CancellationToken cancellationToken);

    /// <summary>
    ///     The trade whose bot this is, locked for update; null for any other bot.
    /// </summary>
    Task<ScanTrade?> GetByBotForUpdate(BotId botId, CancellationToken cancellationToken);

    Task<ScanTrade?> Get(ScanTradeId id, CancellationToken cancellationToken);

    Task<ScanTrade?> GetForUpdate(ScanTradeId id, CancellationToken cancellationToken);

    /// <summary>
    ///     Whether the owner has a live trade on the pair.
    /// </summary>
    Task<bool> HasLive(UserId ownerId, VenueId venue, string symbol, CancellationToken cancellationToken);

    /// <summary>
    ///     Every live trade of every owner, oldest first.
    /// </summary>
    Task<IReadOnlyList<ScanTrade>> ListLive(int limit, CancellationToken cancellationToken);

    /// <summary>
    ///     Every finished trade whose bot was not released yet, oldest first.
    /// </summary>
    Task<IReadOnlyList<ScanTrade>> ListUnreleased(int limit, CancellationToken cancellationToken);

    /// <summary>
    ///     The owner's trades on a venue, newest first, on one pair when <paramref name="symbol" /> is given.
    /// </summary>
    Task<IReadOnlyList<ScanTrade>> List(UserId ownerId, VenueId venue, string? symbol, int limit, CancellationToken cancellationToken);
}