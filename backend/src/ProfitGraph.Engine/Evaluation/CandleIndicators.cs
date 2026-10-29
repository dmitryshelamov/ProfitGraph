using ProfitGraph.Engine.Market;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Индикаторы с decimal-границей. Каждый вызов продвигает состояние ровно на одну закрытую свечу;
///     вызывающий отвечает за то, чтобы чтение на событиях не продвигало историю.
/// </summary>
internal static class CandleIndicators
{
    public static EngineValue Sma(CandleIndicatorState state, EngineValue sample, int period, ValueType type, DateTime timestamp)
    {
        state.Buffer ??= new LibraryIndicatorBuffer(period);
        decimal? value = state.Buffer.Add(timestamp, sample.IsAvailable ? sample.Number : null);
        state.Ready = value.HasValue;

        return value.HasValue ? EngineValue.Numeric(type, value.Value) : EngineValue.Unavailable(type);
    }

    /// <summary>
    ///     Экспоненциальная средняя в decimal; первое значение — простое среднее первых period отсчётов.
    /// </summary>
    public static EngineValue Ema(CandleIndicatorState state, EngineValue sample, int period, ValueType type)
    {
        state.Ema ??= new ExponentialMovingAverage(period);

        return Ready(state, state.Ema.Add(sample.IsAvailable ? sample.Number : null), type);
    }

    /// <summary>
    ///     RSI Уайлдера в decimal; первое значение — после period изменений, то есть period + 1 отсчётов.
    /// </summary>
    public static EngineValue Rsi(CandleIndicatorState state, EngineValue sample, int period)
    {
        state.Rsi ??= new WilderRelativeStrength(period);

        return Ready(state, state.Rsi.Add(sample.IsAvailable ? sample.Number : null), ValueType.Scalar);
    }

    /// <summary>
    ///     Стандартное отклонение генеральной совокупности за последние period отсчётов в decimal.
    /// </summary>
    public static EngineValue StandardDeviation(CandleIndicatorState state, EngineValue sample, int period, ValueType type)
    {
        state.Deviation ??= new RollingStandardDeviation(period);

        return Ready(state, state.Deviation.Add(sample.IsAvailable ? sample.Number : null), type);
    }

    public static EngineValue Previous(CandleIndicatorState state, EngineValue sample, ValueType type)
    {
        EngineValue value = state.PreviousA ?? EngineValue.Unavailable(type);
        state.PreviousA = sample;
        state.Ready = value.IsAvailable;

        return value;
    }

    /// <summary>
    ///     Пересечение вверх: предыдущее A ≤ B и текущее A > B; вниз симметрично. Нужны доступные прошлые и текущие значения.
    /// </summary>
    public static EngineValue Crossover(CandleIndicatorState state, EngineValue a, EngineValue b, bool above)
    {
        bool ready = state.PreviousA is { IsAvailable: true } && state.PreviousB is { IsAvailable: true } && a.IsAvailable && b.IsAvailable;
        state.Ready = ready;
        bool crossed = ready && (above
            ? state.PreviousA!.Value.Number <= state.PreviousB!.Value.Number && a.Number > b.Number
            : state.PreviousA!.Value.Number >= state.PreviousB!.Value.Number && a.Number < b.Number);
        state.PreviousA = a;
        state.PreviousB = b;

        return EngineValue.FromBoolean(crossed);
    }

    /// <summary>
    ///     Простое среднее true range; первая свеча без предыдущего close в окно не попадает.
    /// </summary>
    public static EngineValue Atr(CandleIndicatorState state, Candle candle, Candle? previous, int period, ValueType type)
    {
        state.Buffer ??= new LibraryIndicatorBuffer(period);
        decimal? value = previous is not null ? state.Buffer.AddTrueRange(candle.OpenTime.UtcDateTime, candle, previous) : null;
        state.Ready = value.HasValue;

        return value.HasValue ? EngineValue.Numeric(type, value.Value) : EngineValue.Unavailable(type);
    }

    private static EngineValue Ready(CandleIndicatorState state, decimal? value, ValueType type)
    {
        state.Ready = value.HasValue;

        return value.HasValue ? EngineValue.Numeric(type, value.Value) : EngineValue.Unavailable(type);
    }
}