using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.ChangeBotParameters;

public sealed record ChangeBotParametersCommand(Guid OwnerId, Guid Id, IReadOnlyDictionary<string, decimal>? Parameters, decimal AllocationCap)
    : ICommand<BotInternal>;