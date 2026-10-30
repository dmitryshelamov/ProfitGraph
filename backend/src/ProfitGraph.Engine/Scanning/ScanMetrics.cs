namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     Метрики одной пары сканера (ADR 0059, раздел 3). Проценты — в процентах, не в долях; метрика без достаточной
///     истории пуста.
/// </summary>
public sealed record ScanMetrics
{
    public decimal? Price { get; init; }
    public decimal? Change5m { get; init; }
    public decimal? Change15m { get; init; }
    public decimal? Change1h { get; init; }
    public decimal? Change4h { get; init; }
    public decimal? Change24h { get; init; }
    public decimal? Turnover1h { get; init; }
    public decimal? Turnover24h { get; init; }
    public decimal? VolumeSpike { get; init; }

    /// <summary>
    ///     Оборот последних 5 и 15 минут к той же медиане часового оборота, пересчитанной на 5 и 15 минут: всплеск объёма в
    ///     начале движения, который часовой всплеск ещё размывает.
    /// </summary>
    public decimal? VolumeSpike5m { get; init; }

    public decimal? VolumeSpike15m { get; init; }
    public decimal? Natr24h { get; init; }
    public decimal? EfficiencyRatio24h { get; init; }
    public decimal? DistanceToHigh24h { get; init; }
    public decimal? DistanceToLow24h { get; init; }
    public decimal? DistanceToHigh7d { get; init; }
    public decimal? DistanceToLow7d { get; init; }

    /// <summary>
    ///     Расстояние от закрытия до максимума 4 часов перед последними 15 минутами: выше нуля — цена вышла из диапазона. Диапазон —
    ///     240 минут со сделками, так что ночь и выходные биржи не сужают его до пустых минут.
    /// </summary>
    public decimal? Breakout4h { get; init; }

    /// <summary>
    ///     Расстояние от закрытия до минимума того же диапазона: ниже нуля — цена вышла из диапазона вниз.
    /// </summary>
    public decimal? Breakdown4h { get; init; }

    /// <summary>
    ///     Ширина того же диапазона, от минимума до максимума, в % от минимума: узкий диапазон — сжатие перед пробоем.
    /// </summary>
    public decimal? Range4h { get; init; }

    /// <summary>
    ///     Ближайшее сопротивление выше цены и ближайшая поддержка ниже неё (ADR 0059, раздел 8), цены уровней.
    /// </summary>
    public decimal? Resistance { get; init; }

    public decimal? Support { get; init; }
    public decimal? DistanceToResistance { get; init; }
    public decimal? DistanceToSupport { get; init; }

    /// <summary>
    ///     Расстояние до сопротивления, делённое на расстояние до поддержки: прибыль к риску лонга от текущей цены.
    /// </summary>
    public decimal? RewardRisk { get; init; }

    /// <summary>
    ///     Цена к EMA последних 50 часовых закрытий, % (ADR 0071, раздел 3): выше нуля — цена над средней.
    /// </summary>
    public decimal? TrendEma50 { get; init; }

    /// <summary>
    ///     EMA 20 часов к EMA 50 часов, %: выше нуля — короткая средняя над длинной, тренд вверх.
    /// </summary>
    public decimal? EmaSpread20x50 { get; init; }

    /// <summary>
    ///     RSI Уайлдера по 14 часовым закрытиям, от 0 до 100.
    /// </summary>
    public decimal? Rsi1h { get; init; }

    /// <summary>
    ///     <see cref="TrendEma50" /> опорной пары биржи, одинаковый у всех строк снимка: куда идёт рынок.
    /// </summary>
    public decimal? MarketTrend { get; init; }

    public decimal? Spread { get; init; }
    public int? ListingAgeDays { get; init; }
    public bool Stale { get; init; }
}