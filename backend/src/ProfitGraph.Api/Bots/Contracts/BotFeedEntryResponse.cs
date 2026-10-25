using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Api.Bots.Contracts;

public sealed record BotFeedEntryResponse(long Sequence, Guid Id, BotFeedKind Kind, IReadOnlyDictionary<string, string> Details, DateTimeOffset OccurredAt);