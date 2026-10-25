using System.Text.Json.Serialization;

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
    decimal Volume)
{
    /// <summary>
    ///     В интервале не было сделок, хотя рынок был открыт (ADR 0023, TC2): все цены равны последнему реальному закрытию,
    ///     объём нулевой. Индикаторы и события закрытия обрабатывают такую свечу как обычную, а replay не исполняет на ней
    ///     ни одного ордера. Значение по умолчанию не пишется в JSON, поэтому сериализованные свечи, чекпоинты и
    ///     закреплённые хэши replay без таких свечей не меняются.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NoTrades { get; init; }
}