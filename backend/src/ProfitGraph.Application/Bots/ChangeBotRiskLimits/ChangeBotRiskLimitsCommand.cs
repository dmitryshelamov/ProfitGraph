using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.ChangeBotRiskLimits;

public sealed record ChangeBotRiskLimitsCommand(Guid OwnerId, Guid Id, decimal MaxOrderQuote, decimal DailyLossLimitQuote) : ICommand<BotInternal>;