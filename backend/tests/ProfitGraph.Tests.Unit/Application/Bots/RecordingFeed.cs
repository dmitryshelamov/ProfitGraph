using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Feed;

namespace ProfitGraph.Tests.Unit.Application.Bots;

internal sealed class RecordingFeed : IBotFeedRepository
{
    public List<BotFeedEntry> Entries { get; } = [];

    public Task Add(BotFeedEntry entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SequencedBotFeedEntry>> List(BotId botId, long? before, int limit, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<int> DeleteOlderThan(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}