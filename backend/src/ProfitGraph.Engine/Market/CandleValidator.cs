namespace ProfitGraph.Engine.Market;

/// <summary>
///     Проверяет физическую согласованность OHLCV и принадлежность времени сетке выбранного интервала; свеча без сделок
///     плоская и без объёма.
/// </summary>
public static class CandleValidator
{
    public static bool IsValid(Candle candle, Timeframe timeframe)
    {
        return candle.OpenTime.Offset == TimeSpan.Zero && timeframe.IsAligned(candle.OpenTime) &&
            candle.OpenTime <= DateTimeOffset.MaxValue.Subtract(timeframe.Duration) &&
            candle.Open > 0 && candle.High > 0 && candle.Low > 0 && candle.Close > 0 && candle.Volume >= 0 &&
            candle.High >= Math.Max(candle.Open, candle.Close) && candle.Low <= Math.Min(candle.Open, candle.Close) &&
            candle.High >= candle.Low &&
            (!candle.NoTrades || (candle.Volume == 0 && candle.Open == candle.High && candle.Open == candle.Low && candle.Open == candle.Close));
    }
}