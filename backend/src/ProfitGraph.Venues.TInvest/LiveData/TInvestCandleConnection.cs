using Microsoft.Extensions.Logging;

using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.LiveData;

/// <summary>
///     One <c>MarketDataStreamService/MarketDataStream</c> connection of a token, shared by every candle subscription of that
///     token (the broker limits the streams per token). Subscribing and unsubscribing are requests on the open stream. When the
///     stream is lost or the broker refuses a subscription, the affected subscriptions end with that error and the connection,
///     once lost, accepts no more.
/// </summary>
internal sealed class TInvestCandleConnection(TInvestTransport transport, TInvestToken token, ILogger logger)
{
    private readonly CancellationTokenSource _closing = new CancellationTokenSource();
    private readonly List<TInvestCandleSubscription> _subscriptions = [];
    private readonly Lock _gate = new Lock();
    private Task<TInvestDuplexStream<MarketDataRequest, MarketDataResponse>> _opening = null!;
    private bool _lost;

    public bool IsLost
    {
        get
        {
            lock (_gate)
            {
                return _lost;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _subscriptions.Count;
            }
        }
    }

    /// <summary>
    ///     Starts opening the stream; every later <see cref="Add" /> waits for it, so subscribers need not coordinate.
    /// </summary>
    public void Open()
    {
        _opening = OpenStream();
    }

    public async Task Add(TInvestCandleSubscription subscription, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_lost)
            {
                throw new IOException("The market data stream is closed.");
            }

            _subscriptions.Add(subscription);
        }

        try
        {
            await Send(SubscriptionAction.Subscribe, subscription, cancellationToken);
        }
        catch
        {
            Remove(subscription);

            throw;
        }
    }

    /// <summary>
    ///     Ends the subscription and asks the broker to stop it; returns whether the connection has no subscription left.
    /// </summary>
    public async Task<bool> Release(TInvestCandleSubscription subscription)
    {
        bool known = Remove(subscription);

        if (known && !IsLost)
        {
            try
            {
                await Send(SubscriptionAction.Unsubscribe, subscription, _closing.Token);
            }
            catch (Exception exception) when (exception is TInvestRpcException or IOException or OperationCanceledException)
            {
                // The stream is gone or closing; the broker drops its subscriptions with it.
            }
        }

        return Count == 0;
    }

    public void Close()
    {
        Lose(new IOException("The market data stream was closed."));
    }

    private async Task<TInvestDuplexStream<MarketDataRequest, MarketDataResponse>> OpenStream()
    {
        try
        {
            TInvestDuplexStream<MarketDataRequest, MarketDataResponse> stream = await transport.Duplex(
                token,
                TInvestMethodGroup.MarketData,
                (invoker, options) => new MarketDataStreamService.MarketDataStreamServiceClient(invoker).MarketDataStream(options),
                _closing.Token);
            _ = Read(stream);

            return stream;
        }
        catch (Exception exception)
        {
            Lose(exception);

            throw;
        }
    }

    private bool Remove(TInvestCandleSubscription subscription)
    {
        lock (_gate)
        {
            return _subscriptions.Remove(subscription);
        }
    }

    private async Task Send(SubscriptionAction action, TInvestCandleSubscription subscription, CancellationToken cancellationToken)
    {
        var request = new SubscribeCandlesRequest { SubscriptionAction = action };
        request.Instruments.Add(new CandleInstrument { InstrumentId = subscription.InstrumentUid, Interval = subscription.Interval });

        TInvestDuplexStream<MarketDataRequest, MarketDataResponse> stream = await _opening.WaitAsync(cancellationToken);
        await stream.Send(new MarketDataRequest { SubscribeCandlesRequest = request }, cancellationToken);
    }

    private async Task Read(TInvestDuplexStream<MarketDataRequest, MarketDataResponse> stream)
    {
        try
        {
            await foreach (MarketDataResponse message in stream.Read(_closing.Token))
            {
                if (message.Candle is { } candle)
                {
                    Deliver(candle);
                }
                else if (message.SubscribeCandlesResponse is { } response)
                {
                    Refuse(response);
                }
            }

            Lose(new IOException("The broker closed the market data stream."));
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            Lose(new IOException("The market data stream was closed."));
        }
        catch (Exception exception)
        {
            logger.LogWarning("The market data stream ended with {ExceptionType}.", exception.GetType().Name);
            Lose(exception);
        }
    }

    private void Deliver(Candle candle)
    {
        lock (_gate)
        {
            foreach (TInvestCandleSubscription subscription in _subscriptions.Where(subscription => subscription.Matches(candle)))
            {
                subscription.Inbox.Writer.TryWrite(candle);
            }
        }
    }

    private void Refuse(SubscribeCandlesResponse response)
    {
        lock (_gate)
        {
            foreach (CandleSubscription refused in response.CandlesSubscriptions.Where(item => item.SubscriptionStatus != SubscriptionStatus.Success))
            {
                foreach (TInvestCandleSubscription subscription in _subscriptions.Where(subscription =>
                             subscription.InstrumentUid == refused.InstrumentUid && subscription.Interval == refused.Interval))
                {
                    subscription.Inbox.Writer.TryComplete(new IOException("T-Invest refused the candle subscription."));
                }
            }
        }
    }

    private void Lose(Exception reason)
    {
        lock (_gate)
        {
            if (_lost)
            {
                return;
            }

            _lost = true;

            foreach (TInvestCandleSubscription subscription in _subscriptions)
            {
                subscription.Inbox.Writer.TryComplete(reason);
            }
        }

        _closing.Cancel();

        if (_opening is { IsCompletedSuccessfully: true })
        {
            _opening.Result.Dispose();
        }
    }
}