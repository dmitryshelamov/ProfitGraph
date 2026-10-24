using MediatR;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Bots.MarkFeedStale;

internal sealed class MarkFeedStaleCommandHandler(IBotRepository bots, TimeProvider clock) : IRequestHandler<MarkFeedStaleCommand>
{
    public async Task Handle(MarkFeedStaleCommand command, CancellationToken cancellationToken)
    {
        var series = new CandleSeries(new VenueId(command.Venue), new InstrumentSymbol(command.Symbol), Timeframe.Parse(command.Timeframe));
        DateTimeOffset now = clock.GetUtcNow();

        foreach (BotId id in await bots.ListDesiredRunningOnSeries(series, cancellationToken))
        {
            Bot? bot = await bots.GetForUpdate(id, cancellationToken);

            if (bot is not null && bot.MarkFeedStale(command.Stale, now))
            {
                await bots.Update(bot, cancellationToken);
            }
        }
    }
}