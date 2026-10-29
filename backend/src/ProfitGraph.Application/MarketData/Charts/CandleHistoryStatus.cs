namespace ProfitGraph.Application.MarketData.Charts;

/// <summary>
///     How completely the venue's history covers a requested range of a chart.
/// </summary>
public enum CandleHistoryStatus
{
    /// <summary>
    ///     Every interval of the range is a stored candle or lies in a market closure the venue reported.
    /// </summary>
    Complete,

    /// <summary>
    ///     The venue's history has intervals without a candle, such as an hour without sales; the candles it has are shown
    ///     but not stored, so a backtest never treats the range as complete.
    /// </summary>
    Partial,

    /// <summary>
    ///     The venue could not be read; only the candles already stored are shown.
    /// </summary>
    Unavailable
}