using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Contracts;

public sealed record BotFeedEntryInternal(long Sequence, Guid Id, BotFeedKind Kind, IReadOnlyDictionary<string, string> Details, DateTimeOffset OccurredAt);