namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Итог подготовки, прикреплённый к прогону один раз: снятые правила инструмента, разрешённый интервал [From, To),
///     идентичность подготовленной истории и разбиение на прогрев и replay.
/// </summary>
public sealed record BacktestHistoryInfo(
    string Identity,
    DateTimeOffset HistoryFrom,
    int WarmupCount,
    int ReplayCount,
    BacktestMarketSnapshot Market,
    DateTimeOffset From,
    DateTimeOffset To);