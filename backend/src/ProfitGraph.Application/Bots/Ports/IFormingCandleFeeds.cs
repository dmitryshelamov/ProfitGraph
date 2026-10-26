using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     The forming-candle feeds of this process: one upstream connection per series (and credential) however many client
///     connections follow it, closed when the last client leaves.
/// </summary>
public interface IFormingCandleFeeds
{
    /// <summary>
    ///     Joins <paramref name="connectionId" /> to the feed of <paramref name="key" />, opening it when it is the first; a
    ///     connection that followed another feed leaves it. <paramref name="access" /> is the market data credential the
    ///     feed's venue reads with, if it needs one.
    /// </summary>
    Task<FormingFeedSnapshotInternal> Join(FormingFeedKeyInternal key, ExchangeCredentialSecretsInternal? access, string connectionId);

    /// <summary>
    ///     Removes the connection from the feed it follows, if any.
    /// </summary>
    Task Leave(string connectionId);
}