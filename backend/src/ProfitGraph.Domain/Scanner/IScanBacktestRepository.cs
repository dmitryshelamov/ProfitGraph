using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Domain.Scanner;

public interface IScanBacktestRepository
{
    Task<ScanBacktest?> Get(ScanBacktestId id, UserId ownerId, CancellationToken cancellationToken);

    Task<ScanBacktest?> GetForUpdate(ScanBacktestId id, CancellationToken cancellationToken);

    Task Add(ScanBacktest backtest, CancellationToken cancellationToken);

    Task Update(ScanBacktest backtest, CancellationToken cancellationToken);
}