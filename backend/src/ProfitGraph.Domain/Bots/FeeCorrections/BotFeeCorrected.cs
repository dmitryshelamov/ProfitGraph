using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.FeeCorrections;

public sealed record BotFeeCorrected(BotId BotId, BotFeeCorrectionId CorrectionId, long EngineOrderId, decimal PreviousFee, decimal CorrectedFee, DateTimeOffset At)
    : IDomainEvent;