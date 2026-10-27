using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Bots change under a row lock: a writer loads the bot with <see cref="GetForUpdate" /> in its transaction, so the
///     owner's commands and the runtime never overwrite each other.
/// </summary>
public interface IBotRepository
{
    Task<Bot?> Get(BotId id, CancellationToken cancellationToken);

    /// <summary>
    ///     Loads the bot and locks its row until the transaction ends.
    /// </summary>
    Task<Bot?> GetForUpdate(BotId id, CancellationToken cancellationToken);

    /// <summary>
    ///     The owner's bots, newest first.
    /// </summary>
    Task<IReadOnlyList<Bot>> ListByOwner(UserId ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BotId>> ListDesiredRunning(CancellationToken cancellationToken);

    /// <summary>
    ///     The bots that should run and are degraded with an unresolved order, which only a check of the venue or the owner can
    ///     clear (ADR 0030).
    /// </summary>
    Task<IReadOnlyList<BotId>> ListDegradedOnUnresolvedOrder(CancellationToken cancellationToken);

    /// <summary>
    ///     The distinct instruments of the bots that should run.
    /// </summary>
    Task<IReadOnlyList<LiveInstrument>> ListDesiredRunningInstruments(CancellationToken cancellationToken);

    /// <summary>
    ///     The bots of a strategy that should run, oldest first: the bots a newly saved version reaches.
    /// </summary>
    Task<IReadOnlyList<Bot>> ListDesiredRunningOnStrategy(StrategyId strategyId, CancellationToken cancellationToken);

    /// <summary>
    ///     The bots that should run on one candle series.
    /// </summary>
    Task<IReadOnlyList<BotId>> ListDesiredRunningOnSeries(CandleSeries series, CancellationToken cancellationToken);

    /// <summary>
    ///     Whether any bot trades through the credential, which then cannot be deleted.
    /// </summary>
    Task<bool> AnyUsesCredential(ExchangeCredentialId credentialId, CancellationToken cancellationToken);

    /// <summary>
    ///     The bots of a credential that should run.
    /// </summary>
    Task<IReadOnlyList<BotId>> ListDesiredRunningOnCredential(ExchangeCredentialId credentialId, CancellationToken cancellationToken);

    Task Add(Bot bot, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes a bot loaded with <see cref="GetForUpdate" /> in the current transaction.
    /// </summary>
    Task Update(Bot bot, CancellationToken cancellationToken);
}