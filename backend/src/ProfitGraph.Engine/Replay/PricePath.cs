using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Неизменяемый выбранный путь Open → Low → High → Close. Нулевые сегменты пропускаются,
///     а Open и Close сохраняются для обработки отдельных границ свечи.
/// </summary>
public sealed class PricePath
{
    public decimal Open { get; }
    public decimal Close { get; }
    public IReadOnlyList<PriceSegment> Segments { get; }

    private PricePath(decimal open, decimal close, IReadOnlyList<PriceSegment> segments)
    {
        Open = open;
        Close = close;
        Segments = segments;
    }

    internal static void Validate(Candle candle)
    {
        ArgumentNullException.ThrowIfNull(candle);

        if (candle.Low <= 0m || candle.High < candle.Low || candle.Open < candle.Low || candle.Open > candle.High
            || candle.Close < candle.Low || candle.Close > candle.High)
        {
            throw new ArgumentException("The OHLC candle must contain positive prices within its low and high.", nameof(candle));
        }
    }

    public static PricePath Build(Candle candle)
    {
        Validate(candle);

        var segments = new List<PriceSegment>(capacity: 3);
        Add(candle.Open, candle.Low, PricePhase.OpenToLow);
        Add(candle.Low, candle.High, PricePhase.LowToHigh);
        Add(candle.High, candle.Close, PricePhase.HighToClose);

        return new PricePath(candle.Open, candle.Close, segments.AsReadOnly());

        void Add(decimal start, decimal end, PricePhase phase)
        {
            if (start != end)
            {
                segments.Add(new PriceSegment(start, end, phase));
            }
        }
    }
}