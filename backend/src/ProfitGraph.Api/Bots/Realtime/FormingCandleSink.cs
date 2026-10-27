using Microsoft.AspNetCore.SignalR;

using ProfitGraph.Api.Bots.Converters;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.JoinBotFormingFeed;
using ProfitGraph.Application.Bots.Ports;

namespace ProfitGraph.Api.Bots.Realtime;

/// <summary>
///     Delivers a forming-candle feed's candles and state changes to the clients in its group (ADR 0026).
/// </summary>
public sealed class FormingCandleSink(IHubContext<FormingCandleHub> hub) : IFormingCandleSink
{
    public Task Candle(FormingFeedKeyInternal key, FormingCandleInternal candle, CancellationToken cancellationToken)
    {
        return hub.Clients.Group(FormingCandleHub.Group(key)).SendAsync("FormingCandle", candle.ToResponse(key), cancellationToken);
    }

    public Task Status(FormingFeedKeyInternal key, FormingFeedStatus status, CancellationToken cancellationToken)
    {
        return hub.Clients.Group(FormingCandleHub.Group(key)).SendAsync("FormingStatus", status.ToResponse(key), cancellationToken);
    }
}