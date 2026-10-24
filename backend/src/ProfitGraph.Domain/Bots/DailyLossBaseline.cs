namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The bot's equity at its first live candle root of a UTC day; the daily loss is measured from it.
/// </summary>
public sealed record DailyLossBaseline(DateOnly Day, decimal Equity);