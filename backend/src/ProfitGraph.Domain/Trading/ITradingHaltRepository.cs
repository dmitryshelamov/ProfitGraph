namespace ProfitGraph.Domain.Trading;

public interface ITradingHaltRepository
{
    Task<TradingHalt> Get(CancellationToken cancellationToken);

    /// <summary>
    ///     Loads the halt and locks it, so submits that check it wait for a halt in progress.
    /// </summary>
    Task<TradingHalt> GetForUpdate(CancellationToken cancellationToken);

    Task Update(TradingHalt halt, CancellationToken cancellationToken);
}