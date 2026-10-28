using ProfitGraph.Domain.Bots.FeeCorrections;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

internal static class BotFeeCorrectionDbConverter
{
    public static BotFeeCorrectionDb ToDb(this BotFeeCorrection correction)
    {
        return new BotFeeCorrectionDb
        {
            Id = correction.Id.Value,
            BotId = correction.BotId.Value,
            EngineOrderId = correction.EngineOrderId,
            PreviousFee = correction.PreviousFee,
            CorrectedFee = correction.CorrectedFee,
            CorrectedAt = correction.CorrectedAt
        };
    }
}