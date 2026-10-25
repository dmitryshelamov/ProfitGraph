using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Contracts;

public sealed record BotOrderWaitingInternal(long EngineOrderId, OrderWaitingCode Code, DateTimeOffset Since, DateTimeOffset? Until, decimal? Low, decimal? High);