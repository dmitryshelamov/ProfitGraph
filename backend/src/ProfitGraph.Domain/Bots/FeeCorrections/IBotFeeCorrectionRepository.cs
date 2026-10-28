namespace ProfitGraph.Domain.Bots.FeeCorrections;

public interface IBotFeeCorrectionRepository
{
    /// <summary>
    ///     Records the correction unless the order's same corrected fee is already recorded; returns whether it was new.
    /// </summary>
    Task<bool> TryAdd(BotFeeCorrection correction, CancellationToken cancellationToken);
}