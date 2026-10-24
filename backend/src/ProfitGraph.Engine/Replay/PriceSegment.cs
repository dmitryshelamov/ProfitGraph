using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Ненулевой направленный участок логического пути свечи без вымышленных временных меток.
/// </summary>
public sealed record PriceSegment
{
    public decimal Start { get; }
    public decimal End { get; }
    public PricePhase Phase { get; }

    public PriceSegment(decimal start, decimal end, PricePhase phase)
    {
        if (start <= 0m || end <= 0m || start == end)
        {
            throw new ArgumentException("A price segment requires distinct positive endpoints.");
        }

        if (phase is not (PricePhase.OpenToLow or PricePhase.LowToHigh or PricePhase.HighToClose)
            || (phase == PricePhase.LowToHigh ? end < start : end > start))
        {
            throw new ArgumentException("Segment direction must match its candle traversal phase.", nameof(phase));
        }

        Start = start;
        End = end;
        Phase = phase;
    }
}