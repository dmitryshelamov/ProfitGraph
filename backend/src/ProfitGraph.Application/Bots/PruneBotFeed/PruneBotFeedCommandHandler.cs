using MediatR;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.PruneBotFeed;

internal sealed class PruneBotFeedCommandHandler(IBotFeedRepository feed, TimeProvider clock) : IRequestHandler<PruneBotFeedCommand, int>
{
    public Task<int> Handle(PruneBotFeedCommand command, CancellationToken cancellationToken)
    {
        return feed.DeleteOlderThan(clock.GetUtcNow() - BotFeedEntry.Retention, cancellationToken);
    }
}