namespace ProfitGraph.Engine.Market;

/// <summary>
///     Одна свеча OHLCV с временем открытия, ценами и объёмом.
///     Служит исходными данными для DataSource и индикаторов; decimal сохраняет точность до преобразования в числовой ряд ноды.
/// </summary>
public sealed record Candle(
    DateTimeOffset OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);