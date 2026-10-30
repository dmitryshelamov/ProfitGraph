using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Domain.Scanner;

public interface IScanBacktestRepository
{
    Task<ScanBacktest?> Get(ScanBacktestId id, UserId ownerId, CancellationToken cancellationToken);

    Task<ScanBacktest?> GetForUpdate(ScanBacktestId id, CancellationToken cancellationToken);

    Task Add(ScanBacktest backtest, CancellationToken cancellationToken);

    Task Update(ScanBacktest backtest, CancellationToken cancellationToken);

    /// <summary>
    ///     Stores the closed trades of the backtest's cards (ADR 0071, section 5).
    /// </summary>
    Task AddCards(ScanBacktestId id, IReadOnlyList<ScanBacktestCard> cards, CancellationToken cancellationToken);
}