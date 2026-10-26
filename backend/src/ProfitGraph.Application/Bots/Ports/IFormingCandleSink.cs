using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.JoinBotFormingFeed;

namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     Where a feed delivers what it receives: to the clients that follow the feed's key.
/// </summary>
public interface IFormingCandleSink
{
    Task Candle(FormingFeedKeyInternal key, FormingCandleInternal candle, CancellationToken cancellationToken);

    Task Status(FormingFeedKeyInternal key, FormingFeedStatus status, CancellationToken cancellationToken);
}