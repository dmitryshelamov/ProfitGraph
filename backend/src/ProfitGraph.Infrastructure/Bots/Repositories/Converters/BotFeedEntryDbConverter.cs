using System.Text.Json;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

internal static class BotFeedEntryDbConverter
{
    public static SequencedBotFeedEntry ToDomain(this BotFeedEntryDb row)
    {
        return new SequencedBotFeedEntry(
            row.Sequence,
            BotFeedEntry.Restore(
                new BotFeedEntryId(row.Id),
                new BotId(row.BotId),
                Enum.Parse<BotFeedKind>(row.Kind),
                JsonSerializer.Deserialize<Dictionary<string, string>>(row.Details) ?? [],
                row.OccurredAt));
    }

    public static BotFeedEntryDb ToDb(this BotFeedEntry entry)
    {
        return new BotFeedEntryDb
        {
            Id = entry.Id.Value,
            BotId = entry.BotId.Value,
            Kind = entry.Kind.ToString(),
            Details = JsonSerializer.Serialize(entry.Details),
            OccurredAt = entry.OccurredAt
        };
    }
}