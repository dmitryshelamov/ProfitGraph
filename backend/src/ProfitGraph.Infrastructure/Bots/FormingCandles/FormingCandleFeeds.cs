using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.JoinBotFormingFeed;
using ProfitGraph.Application.Bots.Ports;

namespace ProfitGraph.Infrastructure.Bots.FormingCandles;

/// <summary>
///     The forming-candle feeds of this process, keyed by series and credential (ADR 0026): the first client opens the feed's
///     one venue connection, later clients share it and the last one to leave closes it. A client connection follows one feed
///     at a time and leaves it when it joins another or disconnects.
/// </summary>
internal sealed class FormingCandleFeeds(
    IEnumerable<IFormingCandleSource> sources,
    IFormingCandleSink sink,
    FormingCandleOptions options,
    TimeProvider clock,
    ILogger<FormingCandleFeeds> logger) : IFormingCandleFeeds
{
    private readonly Dictionary<FormingFeedKeyInternal, FormingCandleFeed> _feeds = [];
    private readonly Dictionary<string, FormingCandleFeed> _joined = [];
    private readonly Lock _gate = new Lock();

    public Task<FormingFeedSnapshotInternal> Join(FormingFeedKeyInternal key, ExchangeCredentialSecretsInternal? access, string connectionId)
    {
        IFormingCandleSource? source = sources.FirstOrDefault(candidate => candidate.Venue.Value == key.Venue);

        if (source is null)
        {
            return Task.FromResult(new FormingFeedSnapshotInternal(key, FormingFeedStatus.Unsupported, Candle: null));
        }

        lock (_gate)
        {
            Release(connectionId);

            if (!_feeds.TryGetValue(key, out FormingCandleFeed? feed))
            {
                feed = new FormingCandleFeed(key, source, access, sink, options, clock, logger);
                _feeds.Add(key, feed);
                feed.Start();
            }

            _joined[connectionId] = feed;

            return Task.FromResult(feed.Add(connectionId));
        }
    }

    public Task Leave(string connectionId)
    {
        lock (_gate)
        {
            Release(connectionId);
        }

        return Task.CompletedTask;
    }

    private void Release(string connectionId)
    {
        if (_joined.Remove(connectionId, out FormingCandleFeed? feed))
        {
            feed.Remove(connectionId);

            if (feed.Count == 0)
            {
                _feeds.Remove(feed.Key);
                feed.Stop();
            }
        }
    }
}