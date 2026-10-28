using ProfitGraph.Domain.Bots.Feed;

namespace ProfitGraph.Application.Bots.Contracts;

public sealed record BotFeedEntryInternal(long Sequence, Guid Id, BotFeedKind Kind, IReadOnlyDictionary<string, string> Details, DateTimeOffset OccurredAt);