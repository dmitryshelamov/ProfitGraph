using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;

using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.LiveData;

/// <summary>
///     Candle updates of shares over <c>MarketDataStreamService/MarketDataStream</c> (ADR 0026): one connection per token, shared
///     by every subscription of that token and closed with its last one, so the broker's limit on streams per token is not spent
///     by a chart per share. A subscription ends with the error of a lost stream; the caller connects again.
/// </summary>
internal sealed class TInvestCandleStreams(TInvestTransport transport, ILoggerFactory loggers)
{
    private readonly Dictionary<string, TInvestCandleConnection> _connections = [];
    private readonly Lock _gate = new Lock();

    public async IAsyncEnumerable<Candle> Subscribe(
        TInvestToken token,
        string instrumentUid,
        SubscriptionInterval interval,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var subscription = new TInvestCandleSubscription(instrumentUid, interval);
        TInvestCandleConnection connection = await Join(token, subscription, cancellationToken);

        try
        {
            await foreach (Candle candle in subscription.Inbox.Reader.ReadAllAsync(cancellationToken))
            {
                yield return candle;
            }
        }
        finally
        {
            if (await connection.Release(subscription))
            {
                Forget(token, connection);
                connection.Close();
            }
        }
    }

    private async Task<TInvestCandleConnection> Join(TInvestToken token, TInvestCandleSubscription subscription, CancellationToken cancellationToken)
    {
        TInvestCandleConnection connection;

        lock (_gate)
        {
            bool opens = !_connections.TryGetValue(token.Fingerprint, out TInvestCandleConnection? existing) || existing.IsLost;
            connection = opens
                ? _connections[token.Fingerprint] = new TInvestCandleConnection(transport, token, loggers.CreateLogger<TInvestCandleConnection>())
                : existing!;

            if (opens)
            {
                connection.Open();
            }
        }

        try
        {
            await connection.Add(subscription, cancellationToken);
        }
        catch
        {
            Forget(token, connection);
            connection.Close();

            throw;
        }

        return connection;
    }

    private void Forget(TInvestToken token, TInvestCandleConnection connection)
    {
        lock (_gate)
        {
            if (_connections.TryGetValue(token.Fingerprint, out TInvestCandleConnection? current) && ReferenceEquals(current, connection))
            {
                _connections.Remove(token.Fingerprint);
            }
        }
    }
}