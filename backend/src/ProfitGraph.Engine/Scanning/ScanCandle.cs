namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     Закрытая свеча сканера: OHLCV и оборот в котируемой валюте за интервал (ADR 0059). Минута без сделок плоская,
///     с нулевыми объёмом и оборотом.
/// </summary>
public sealed record ScanCandle(
    DateTimeOffset OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal Turnover);