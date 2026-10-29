namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     Всё, из чего считаются метрики одной пары сканера: закрытые минутные и часовые свечи по возрастанию времени без
///     пропусков, цена формирующейся минуты, лучшие цены покупки и продажи, время листинга и момент расчёта.
/// </summary>
public sealed record ScanInput(
    IReadOnlyList<ScanCandle> Minutes,
    IReadOnlyList<ScanCandle> Hours,
    decimal? LastPrice,
    decimal? BestBid,
    decimal? BestAsk,
    DateTimeOffset? ListedAt,
    DateTimeOffset Now);