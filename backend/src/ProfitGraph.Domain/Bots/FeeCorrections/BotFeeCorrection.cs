using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.FeeCorrections;

/// <summary>
///     A commission the venue reported after an order's fills were accounted (ADR 0023, TC4): the order's cumulative fee
///     before and after. It is recorded beside the fills, which stay as they were; only an increase is a correction, and the
///     same corrected fee of an order is recorded once.
/// </summary>
public sealed class BotFeeCorrection : AggregateRoot
{
    public BotFeeCorrectionId Id { get; }
    public BotId BotId { get; }
    public long EngineOrderId { get; }
    public decimal PreviousFee { get; }
    public decimal CorrectedFee { get; }
    public DateTimeOffset CorrectedAt { get; }

    public decimal Delta => CorrectedFee - PreviousFee;

    private BotFeeCorrection(BotFeeCorrectionId id, BotId botId, long engineOrderId, decimal previousFee, decimal correctedFee, DateTimeOffset correctedAt)
    {
        if (previousFee < 0m || correctedFee <= previousFee)
        {
            throw new ArgumentException("A fee correction raises a nonnegative fee.");
        }

        Id = id;
        BotId = botId;
        EngineOrderId = engineOrderId;
        PreviousFee = previousFee;
        CorrectedFee = correctedFee;
        CorrectedAt = correctedAt;
    }

    public static BotFeeCorrection Record(BotFeeCorrectionId id, BotId botId, long engineOrderId, decimal previousFee, decimal correctedFee, DateTimeOffset at)
    {
        var correction = new BotFeeCorrection(id, botId, engineOrderId, previousFee, correctedFee, at);
        correction.AddDomainEvent(new BotFeeCorrected(botId, id, engineOrderId, previousFee, correctedFee, at));

        return correction;
    }

    public static BotFeeCorrection Restore(BotFeeCorrectionId id, BotId botId, long engineOrderId, decimal previousFee, decimal correctedFee, DateTimeOffset correctedAt)
    {
        return new BotFeeCorrection(id, botId, engineOrderId, previousFee, correctedFee, correctedAt);
    }
}