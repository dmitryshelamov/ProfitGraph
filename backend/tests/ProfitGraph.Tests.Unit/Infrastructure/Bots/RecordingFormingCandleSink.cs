using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.JoinBotFormingFeed;
using ProfitGraph.Application.Bots.Ports;

namespace ProfitGraph.Tests.Unit.Infrastructure.Bots;

internal sealed class RecordingFormingCandleSink : IFormingCandleSink
{
    public List<FormingCandleInternal> Candles { get; } = [];

    public List<FormingFeedStatus> Statuses { get; } = [];

    public Task Candle(FormingFeedKeyInternal key, FormingCandleInternal candle, CancellationToken cancellationToken)
    {
        lock (Candles)
        {
            Candles.Add(candle);
        }

        return Task.CompletedTask;
    }

    public Task Status(FormingFeedKeyInternal key, FormingFeedStatus status, CancellationToken cancellationToken)
    {
        lock (Statuses)
        {
            Statuses.Add(status);
        }

        return Task.CompletedTask;
    }
}