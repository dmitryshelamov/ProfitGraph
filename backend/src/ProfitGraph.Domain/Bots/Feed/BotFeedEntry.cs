using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.Feed;

/// <summary>
///     One fact in a bot's event feed (ADR 0021), recorded from its Domain event. Details are short invariant strings by
///     name, never credentials or venue payloads. Entries older than 90 days are removed. It keeps its own copy of the details.
/// </summary>
public sealed class BotFeedEntry : AggregateRoot
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    public BotFeedEntryId Id { get; }
    public BotId BotId { get; }
    public BotFeedKind Kind { get; }
    public IReadOnlyDictionary<string, string> Details { get; }
    public DateTimeOffset OccurredAt { get; }

    private BotFeedEntry(BotFeedEntryId id, BotId botId, BotFeedKind kind, IReadOnlyDictionary<string, string> details, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(details);
        Id = id;
        BotId = botId;
        Kind = kind;
        Details = new Dictionary<string, string>(details, StringComparer.Ordinal).AsReadOnly();
        OccurredAt = occurredAt;
    }

    public static BotFeedEntry Record(BotFeedEntryId id, BotId botId, BotFeedKind kind, IReadOnlyDictionary<string, string> details, DateTimeOffset occurredAt)
    {
        return new BotFeedEntry(id, botId, kind, details, occurredAt);
    }

    public static BotFeedEntry Restore(BotFeedEntryId id, BotId botId, BotFeedKind kind, IReadOnlyDictionary<string, string> details, DateTimeOffset occurredAt)
    {
        return new BotFeedEntry(id, botId, kind, details, occurredAt);
    }
}